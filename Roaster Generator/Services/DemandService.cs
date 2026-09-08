using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Roaster_Generator.Configuration;
using Roaster_Generator.Contracts.Demand;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;

namespace Roaster_Generator.Services;

public sealed class DemandValidationException(string message) : Exception(message);

public sealed class DemandService(
    AppDbContext db,
    IOptions<ShopHoursOptions> shopHoursOptions)
{
    private const decimal DeliveriesPerEmployee = 2.7m;
    private const decimal SundayPayRateMultiplier = 1.25m;
    private readonly ShopHoursOptions shopHours = shopHoursOptions.Value;
    private static readonly string[] DayLabels =
    [
        "Monday",
        "Tuesday",
        "Wednesday",
        "Thursday",
        "Friday",
        "Saturday",
        "Sunday"
    ];

    public async Task<IReadOnlyList<DemandPlanSummaryResponse>> GetPlansAsync(
        CancellationToken cancellationToken)
    {
        return await db.DemandPlans
            .AsNoTracking()
            .OrderByDescending(plan => plan.UpdatedAtUtc)
            .ThenByDescending(plan => plan.WeekStart)
            .Take(1)
            .Select(plan => new DemandPlanSummaryResponse
            {
                Id = plan.Id,
                Name = plan.Name,
                WeekStart = plan.WeekStart,
                UpdatedAtUtc = plan.UpdatedAtUtc,
                RowCount = plan.Rows.Count,
                ColumnCount = plan.Columns.Count,
                HourlyRate = plan.HourlyRate,
                WeeklyTargetSales = plan.Columns.Sum(column => column.TargetSales)
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<DemandPlanResponse?> GetPlanAsync(
        Guid planId,
        CancellationToken cancellationToken)
    {
        var plan = await LoadPlanAsync(planId, cancellationToken);
        return plan is null ? null : ToResponse(plan);
    }

    public async Task<DemandPlanResponse> ImportTextAsync(
        DemandImportRequest request,
        CancellationToken cancellationToken)
    {
        ValidateMetadata(request.Name, request.WeekStart);
        var parsed = ParseText(request.Content);
        return await SaveImportedPlanAsync(request.Name, request.WeekStart, parsed, cancellationToken);
    }

    public async Task<DemandPlanResponse> ImportExcelAsync(
        string? name,
        DateOnly weekStart,
        Stream stream,
        CancellationToken cancellationToken)
    {
        ValidateMetadata(string.IsNullOrWhiteSpace(name) ? "Imported demand" : name, weekStart);

        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheets.FirstOrDefault()
            ?? throw new DemandValidationException("The Excel file does not contain a worksheet.");
        var lastColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;

        if (lastColumn == 0)
        {
            throw new DemandValidationException("The Excel worksheet is empty.");
        }

        var rows = worksheet.RowsUsed()
            .Select(row => row.Cells(1, lastColumn)
                .Select(cell => cell.Value.ToString())
                .ToArray())
            .ToList();

        var parsed = ParseRows(rows);
        var planName = string.IsNullOrWhiteSpace(name) ? "Imported demand" : name.Trim();
        return await SaveImportedPlanAsync(planName, weekStart, parsed, cancellationToken);
    }

    public async Task<DemandPlanResponse> UpdateAsync(
        Guid planId,
        DemandPlanUpdateRequest request,
        CancellationToken cancellationToken)
    {
        ValidateMetadata(request.Name, request.WeekStart);

        if (request.Columns.Count == 0)
        {
            throw new DemandValidationException("At least one demand column is required.");
        }

        var plan = await LoadPlanAsync(planId, cancellationToken)
            ?? throw new DemandValidationException("Demand plan not found.");

        if (request.Columns.GroupBy(column => column.Position).Any(group => group.Count() > 1))
        {
            throw new DemandValidationException("Demand column positions must be unique.");
        }

        var existingPositions = plan.Columns.Select(column => column.Position).ToHashSet();
        var requestedPositions = request.Columns.Select(column => column.Position).ToHashSet();

        if (!existingPositions.SetEquals(requestedPositions))
        {
            throw new DemandValidationException("Demand columns can only be changed by importing a new table.");
        }

        if (request.Rows.GroupBy(row => row.Hour).Any(group => group.Count() > 1))
        {
            throw new DemandValidationException("Demand hours must be unique.");
        }

        var existingHours = plan.Rows.Select(row => row.Hour).ToHashSet();
        var requestedHours = request.Rows.Select(row => row.Hour).ToHashSet();

        if (!existingHours.SetEquals(requestedHours))
        {
            throw new DemandValidationException("Demand hours can only be changed by importing a new table.");
        }

        if (request.Rows.Any(row =>
                row.Values.GroupBy(value => value.Position).Any(group => group.Count() > 1) ||
                !requestedPositions.SetEquals(row.Values.Select(value => value.Position))))
        {
            throw new DemandValidationException("Every demand row must contain each imported day exactly once.");
        }

        if (request.Rows.SelectMany(row => row.Values).Any(value => value.Demand is < 0))
        {
            throw new DemandValidationException("Demand cannot be negative.");
        }

        if (request.HourlyRate < 0)
        {
            throw new DemandValidationException("Hourly rate cannot be negative.");
        }

        if (request.Columns.Any(column => column.TargetSales < 0))
        {
            throw new DemandValidationException("Target sales cannot be negative.");
        }

        var otherPlan = await db.DemandPlans
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.WeekStart == request.WeekStart && item.Id != planId,
                cancellationToken);

        if (otherPlan is not null)
        {
            throw new DemandValidationException(
                "A demand plan already exists for this week. Edit that plan instead.");
        }

        var positionsByColumnId = plan.Columns.ToDictionary(column => column.Id, column => column.Position);
        var existingDeliveries = plan.Rows
            .SelectMany(row => row.Values.Select(value =>
                (row.Hour, Position: positionsByColumnId[value.DemandColumnId], value.Deliveries)))
            .ToDictionary(value => (value.Hour, value.Position), value => value.Deliveries);

        var parsed = new ParsedDemand(
            request.Columns
                .OrderBy(column => column.Position)
                .Select(column => new ParsedDemandColumn(column.Position, GetColumnLabel(column.Position)))
                .ToList(),
            request.Rows
                .OrderBy(row => GetDisplayHourOrder(row.Hour))
                .Select(row => new ParsedDemandRow(
                    row.Hour,
                    row.Values.Select(value =>
                    {
                        if (!existingDeliveries.TryGetValue((row.Hour, value.Position), out var deliveries))
                        {
                            throw new DemandValidationException(
                                $"The demand value for hour {row.Hour:00} and position {value.Position} does not exist.");
                        }

                        return new ParsedDemandValue(value.Position, deliveries, value.Demand);
                    }).ToList()))
                .ToList());

        parsed = NormalizeAndValidateDemand(parsed, request.WeekStart);
        foreach (var parsedRow in parsed.Rows)
        {
            var row = plan.Rows.Single(item => item.Hour == parsedRow.Hour);
            var valuesByPosition = row.Values.ToDictionary(
                value => positionsByColumnId[value.DemandColumnId]);

            foreach (var parsedValue in parsedRow.Values)
            {
                valuesByPosition[parsedValue.Position].Demand = parsedValue.Demand;
            }
        }

        plan.Name = request.Name.Trim();
        plan.WeekStart = request.WeekStart;
        plan.HourlyRate = request.HourlyRate;
        foreach (var columnRequest in request.Columns)
        {
            plan.Columns.Single(column => column.Position == columnRequest.Position).TargetSales =
                columnRequest.TargetSales;
        }
        plan.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(await LoadPlanAsync(plan.Id, cancellationToken) ?? plan);
    }

    public async Task DeleteAsync(Guid planId, CancellationToken cancellationToken)
    {
        var plan = await db.DemandPlans.SingleOrDefaultAsync(item => item.Id == planId, cancellationToken)
            ?? throw new DemandValidationException("Demand plan not found.");

        db.DemandPlans.Remove(plan);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<DemandPlanResponse> SaveImportedPlanAsync(
        string name,
        DateOnly weekStart,
        ParsedDemand parsed,
        CancellationToken cancellationToken)
    {
        parsed = NormalizeAndValidateDemand(parsed, weekStart);

        var existingPlan = await db.DemandPlans
            .Include(plan => plan.Columns)
            .Include(plan => plan.Rows)
            .ThenInclude(row => row.Values)
            .OrderByDescending(plan => plan.UpdatedAtUtc)
            .ThenByDescending(plan => plan.WeekStart)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingPlan is null)
        {
            var plan = CreatePlan(name, weekStart, parsed);
            db.DemandPlans.Add(plan);
            await db.SaveChangesAsync(cancellationToken);
            return ToResponse(await LoadPlanAsync(plan.Id, cancellationToken) ?? plan);
        }

        await UpdatePlanContentsAsync(existingPlan, name.Trim(), weekStart, parsed, cancellationToken);
        db.ChangeTracker.Clear();
        return ToResponse(await LoadPlanAsync(existingPlan.Id, cancellationToken) ?? existingPlan);
    }

    private async Task UpdatePlanContentsAsync(
        DemandPlan plan,
        string name,
        DateOnly weekStart,
        ParsedDemand parsed,
        CancellationToken cancellationToken)
    {
        plan.Name = name;
        plan.WeekStart = weekStart;
        plan.UpdatedAtUtc = DateTimeOffset.UtcNow;

        var requestedColumnPositions = parsed.Columns
            .Select(column => column.Position)
            .ToHashSet();
        var columnsByPosition = plan.Columns.ToDictionary(column => column.Position);

        foreach (var parsedColumn in parsed.Columns)
        {
            if (columnsByPosition.ContainsKey(parsedColumn.Position))
            {
                continue;
            }

            var column = new DemandColumn
            {
                Id = Guid.NewGuid(),
                DemandPlanId = plan.Id,
                Position = parsedColumn.Position,
                Label = GetColumnLabel(parsedColumn.Position),
                TargetSales = 0m
            };

            plan.Columns.Add(column);
            columnsByPosition[column.Position] = column;
        }

        var staleColumns = plan.Columns
            .Where(column => !requestedColumnPositions.Contains(column.Position))
            .ToList();
        var requestedColumnIds = parsed.Columns
            .Select(column => columnsByPosition[column.Position].Id)
            .ToHashSet();

        foreach (var staleColumn in staleColumns)
        {
            db.DemandColumns.Remove(staleColumn);
        }

        var requestedHours = parsed.Rows
            .Select(row => row.Hour)
            .ToHashSet();
        var rowsByHour = plan.Rows.ToDictionary(row => row.Hour);

        foreach (var parsedRow in parsed.Rows)
        {
            if (!rowsByHour.TryGetValue(parsedRow.Hour, out var row))
            {
                row = new DemandRow
                {
                    Id = Guid.NewGuid(),
                    DemandPlanId = plan.Id,
                    Hour = parsedRow.Hour
                };
                plan.Rows.Add(row);
                rowsByHour[row.Hour] = row;
            }

            foreach (var parsedValue in parsedRow.Values)
            {
                var column = columnsByPosition[parsedValue.Position];
                var value = row.Values.FirstOrDefault(item => item.DemandColumnId == column.Id);

                if (value is null)
                {
                    value = new DemandValue
                    {
                        Id = Guid.NewGuid(),
                        DemandRowId = row.Id,
                        DemandColumnId = column.Id
                    };
                    row.Values.Add(value);
                }

                value.Deliveries = parsedValue.Deliveries;
                value.Demand = parsedValue.Demand;
            }

            var staleValues = row.Values
                .Where(value => !requestedColumnIds.Contains(value.DemandColumnId))
                .ToList();
            db.DemandValues.RemoveRange(staleValues);
        }

        var staleRows = plan.Rows
            .Where(row => !requestedHours.Contains(row.Hour))
            .ToList();
        db.DemandRows.RemoveRange(staleRows);

        await db.SaveChangesAsync(cancellationToken);
    }

    private static DemandPlan CreatePlan(string name, DateOnly weekStart, ParsedDemand parsed)
    {
        var now = DateTimeOffset.UtcNow;
        var plan = new DemandPlan
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            WeekStart = weekStart,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        AddParsedContents(plan, parsed);
        return plan;
    }

    private static void AddParsedContents(DemandPlan plan, ParsedDemand parsed)
    {
        var columnsByPosition = new Dictionary<int, DemandColumn>();

        foreach (var parsedColumn in parsed.Columns)
        {
            var column = new DemandColumn
            {
                Id = Guid.NewGuid(),
                DemandPlanId = plan.Id,
                Position = parsedColumn.Position,
                Label = GetColumnLabel(parsedColumn.Position),
                TargetSales = 0m
            };

            plan.Columns.Add(column);
            columnsByPosition[column.Position] = column;
        }

        foreach (var parsedRow in parsed.Rows)
        {
            var row = new DemandRow
            {
                Id = Guid.NewGuid(),
                DemandPlanId = plan.Id,
                Hour = parsedRow.Hour
            };

            plan.Rows.Add(row);

            foreach (var parsedValue in parsedRow.Values)
            {
                if (!columnsByPosition.TryGetValue(parsedValue.Position, out var column))
                {
                    continue;
                }

                var value = new DemandValue
                {
                    Id = Guid.NewGuid(),
                    DemandRowId = row.Id,
                    DemandColumnId = column.Id,
                    Deliveries = parsedValue.Deliveries,
                    Demand = parsedValue.Demand
                };

                row.Values.Add(value);
                column.Values.Add(value);
            }
        }
    }

    private async Task<DemandPlan?> LoadPlanAsync(Guid planId, CancellationToken cancellationToken)
    {
        return await db.DemandPlans
            .Include(plan => plan.Columns)
            .Include(plan => plan.Rows)
            .ThenInclude(row => row.Values)
            .SingleOrDefaultAsync(plan => plan.Id == planId, cancellationToken);
    }

    private DemandPlanResponse ToResponse(DemandPlan plan)
    {
        var columns = plan.Columns
            .OrderBy(column => column.Position)
            .Select(column => new DemandColumnResponse
            {
                Position = column.Position,
                Label = GetColumnLabel(column.Position),
                TotalHours = CalculateTotalHours(plan, column),
                TargetSales = column.TargetSales
            })
            .ToList();
        var columnsById = plan.Columns.ToDictionary(column => column.Id);
        var dailyLabour = columns
            .Select(column =>
            {
                var requiredDriverHours = column.TotalHours;
                var appliedHourlyRate = AppliedHourlyRate(column.Position, plan.HourlyRate);
                var labourCost = Math.Round(requiredDriverHours * appliedHourlyRate, 2, MidpointRounding.AwayFromZero);
                var labourPercentage = column.TargetSales > 0m
                    ? Math.Round(labourCost / column.TargetSales * 100m, 2, MidpointRounding.AwayFromZero)
                    : (decimal?)null;

                return new DemandLabourDayResponse
                {
                    Position = column.Position,
                    Label = column.Label,
                    TargetSales = column.TargetSales,
                    RequiredDriverHours = requiredDriverHours,
                    AppliedHourlyRate = appliedHourlyRate,
                    LabourCost = labourCost,
                    LabourPercentage = labourPercentage
                };
            })
            .ToList();
        var weeklyTargetSales = dailyLabour.Sum(item => item.TargetSales);
        var weeklyLabourCost = Math.Round(dailyLabour.Sum(item => item.LabourCost), 2, MidpointRounding.AwayFromZero);

        return new DemandPlanResponse
        {
            Id = plan.Id,
            Name = plan.Name,
            WeekStart = plan.WeekStart,
            UpdatedAtUtc = plan.UpdatedAtUtc,
            HourlyRate = plan.HourlyRate,
            WeeklyTargetSales = weeklyTargetSales,
            WeeklyLabourCost = weeklyLabourCost,
            WeeklyLabourPercentage = weeklyTargetSales > 0m
                ? Math.Round(weeklyLabourCost / weeklyTargetSales * 100m, 2, MidpointRounding.AwayFromZero)
                : null,
            Columns = columns,
            DailyLabour = dailyLabour,
            Rows = plan.Rows
                .OrderBy(row => GetDisplayHourOrder(row.Hour))
                .Select(row => new DemandRowResponse
                {
                    Hour = row.Hour,
                    Values = row.Values
                        .Where(value => columnsById.ContainsKey(value.DemandColumnId))
                        .OrderBy(value => columnsById[value.DemandColumnId].Position)
                        .Select(value => new DemandValueResponse
                        {
                            Position = columnsById[value.DemandColumnId].Position,
                            Deliveries = value.Deliveries,
                            Demand = value.Demand
                        })
                        .ToList()
                })
                .ToList()
        };
    }

    private static decimal AppliedHourlyRate(int position, decimal baseHourlyRate) =>
        position == 6
            ? Math.Round(baseHourlyRate * SundayPayRateMultiplier, 2, MidpointRounding.AwayFromZero)
            : baseHourlyRate;

    private static void ValidateMetadata(string? name, DateOnly weekStart)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DemandValidationException("A demand plan name is required.");
        }

        if (weekStart == default)
        {
            throw new DemandValidationException("A week start date is required.");
        }

        if (weekStart.DayOfWeek != DayOfWeek.Monday)
        {
            throw new DemandValidationException("The week start date must be a Monday.");
        }
    }

    private static ParsedDemand ParseText(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new DemandValidationException("Paste the demand table before importing it.");
        }

        var rows = content
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(SplitTextRow)
            .Where(row => row.Length > 0)
            .ToList();

        return ParseRows(rows);
    }

    private static string[] SplitTextRow(string row)
    {
        var trimmed = row.Trim();

        if (trimmed.Contains('|'))
        {
            var cells = trimmed.Split('|').Select(cell => cell.Trim()).ToList();

            if (cells.Count > 0 && cells[0].Length == 0)
            {
                cells.RemoveAt(0);
            }

            if (cells.Count > 0 && cells[^1].Length == 0)
            {
                cells.RemoveAt(cells.Count - 1);
            }

            return cells.ToArray();
        }

        if (trimmed.Contains('\t'))
        {
            return trimmed.Split('\t').Select(cell => cell.Trim()).ToArray();
        }

        return trimmed.Split(',').Select(cell => cell.Trim()).ToArray();
    }

    private static ParsedDemand ParseRows(IReadOnlyList<string[]> rows)
    {
        var dataRows = rows
            .Select(row => new { Cells = row, Hour = ParseHour(row.FirstOrDefault()) })
            .Where(item => item.Hour is not null)
            .Select(item => (item.Cells, Hour: item.Hour!.Value))
            .ToList();

        if (dataRows.Count == 0)
        {
            throw new DemandValidationException(
                "No hourly rows were found. The first column must contain hours from 00 to 23.");
        }

        var maxValueCells = dataRows.Max(item => Math.Max(0, item.Cells.Length - 1));
        var activeIndexes = Enumerable.Range(0, maxValueCells)
            .Where(index => dataRows.Any(item =>
                item.Cells.Length > index + 1 &&
                !string.IsNullOrWhiteSpace(item.Cells[index + 1])))
            .ToList();

        if (activeIndexes.Count == 0)
        {
            throw new DemandValidationException("No demand values were found after the hour column.");
        }

        var columns = Enumerable.Range(0, activeIndexes.Count / 2)
            .Select(position => new ParsedDemandColumn(position, GetColumnLabel(position)))
            .ToList();

        if (activeIndexes.Count % 2 != 0)
        {
            throw new DemandValidationException(
                "Each day must have a pizzas column and a deliveries column. Check the imported table.");
        }

        var parsedRows = new List<ParsedDemandRow>();
        var duplicateHours = dataRows.GroupBy(item => item.Hour).FirstOrDefault(group => group.Count() > 1);

        if (duplicateHours is not null)
        {
            throw new DemandValidationException(
                $"The hour {duplicateHours.Key:00} appears more than once.");
        }

        foreach (var dataRow in dataRows.OrderBy(item => GetDisplayHourOrder(item.Hour)))
        {
            var values = new List<ParsedDemandValue>();

            for (var position = 0; position < columns.Count; position++)
            {
                var deliveries = ParseDecimal(GetCell(dataRow.Cells, activeIndexes, position * 2 + 1));

                values.Add(new ParsedDemandValue(
                    position,
                    deliveries,
                    CalculateDemand(deliveries)));
            }

            parsedRows.Add(new ParsedDemandRow(dataRow.Hour, values));
        }

        return new ParsedDemand(columns, parsedRows);
    }

    private static string? GetCell(
        string[] cells,
        IReadOnlyList<int> activeIndexes,
        int activeIndex)
    {
        if (activeIndex >= activeIndexes.Count)
        {
            return null;
        }

        var sourceIndex = activeIndexes[activeIndex] + 1;
        return sourceIndex < cells.Length ? cells[sourceIndex] : null;
    }

    private static int? ParseHour(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();

        if (TimeOnly.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            return time.Hour;
        }

        return int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hour)
            && hour is >= 0 and <= 23
            ? hour
            : null;
    }

    private static decimal? ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim() == "#")
        {
            return null;
        }

        return decimal.TryParse(
            value.Trim(),
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var result)
            ? result
            : null;
    }

    private static int? CalculateDemand(decimal? deliveries)
    {
        return deliveries is null
            ? null
            : (int)Math.Round(deliveries.Value / DeliveriesPerEmployee, MidpointRounding.AwayFromZero);
    }

    private ParsedDemand NormalizeAndValidateDemand(ParsedDemand parsed, DateOnly weekStart)
    {
        if (parsed.Columns.Count != DayLabels.Length)
        {
            throw new DemandValidationException(
                "The imported table must contain exactly seven day pairs: Monday through Sunday.");
        }

        var valuesByHour = parsed.Rows.ToDictionary(
            row => row.Hour,
            row => row.Values.ToDictionary(value => value.Position));

        foreach (var column in parsed.Columns)
        {
            var lastProvided = parsed.Rows
                .SelectMany(row => row.Values.Select(value => new { row.Hour, Value = value }))
                .Where(item => item.Value.Position == column.Position && HasInput(item.Value))
                .OrderBy(item => GetDisplayHourOrder(item.Hour))
                .LastOrDefault();

            if (lastProvided is null)
            {
                continue;
            }

            var lastOpenHourOrder = GetDisplayHours()
                .Where(hour => IsShopOpen(weekStart, column.Position, hour))
                .Select(GetDisplayHourOrder)
                .DefaultIfEmpty(-1)
                .Max();

            if (GetDisplayHourOrder(lastProvided.Hour) >= lastOpenHourOrder)
            {
                continue;
            }

            foreach (var hour in GetDisplayHours())
            {
                var displayOrder = GetDisplayHourOrder(hour);

                if (displayOrder <= GetDisplayHourOrder(lastProvided.Hour) ||
                    displayOrder > lastOpenHourOrder ||
                    !IsShopOpen(weekStart, column.Position, hour))
                {
                    continue;
                }

                if (!valuesByHour.TryGetValue(hour, out var values))
                {
                    values = parsed.Columns
                        .ToDictionary(
                            item => item.Position,
                            item => new ParsedDemandValue(item.Position, null, null));
                    valuesByHour[hour] = values;
                }

                if (!values.TryGetValue(column.Position, out var currentValue) ||
                    !HasInput(currentValue))
                {
                    values[column.Position] = new ParsedDemandValue(
                        column.Position,
                        lastProvided.Value.Deliveries,
                        lastProvided.Value.Demand);
                }
            }
        }

        var normalizedRows = valuesByHour
            .OrderBy(item => GetDisplayHourOrder(item.Key))
            .Select(item => new ParsedDemandRow(
                item.Key,
                parsed.Columns
                    .OrderBy(column => column.Position)
                    .Select(column =>
                    {
                        var value = item.Value.TryGetValue(column.Position, out var existingValue)
                            ? existingValue
                            : new ParsedDemandValue(column.Position, null, null);

                        return new ParsedDemandValue(
                            column.Position,
                            value.Deliveries,
                            value.Demand);
                    })
                    .ToList()))
            .ToList();
        var normalized = new ParsedDemand(parsed.Columns, normalizedRows);

        return normalized;
    }

    private static bool HasInput(ParsedDemandValue value) =>
        value.Deliveries is not null || value.Demand is not null;

    private static IEnumerable<int> GetDisplayHours() =>
        Enumerable.Range(6, 18).Concat(Enumerable.Range(0, 6));

    private int CalculateTotalHours(DemandPlan plan, DemandColumn column)
    {
        return plan.Rows
            .Where(row => IsShopOpen(plan.WeekStart, column.Position, row.Hour))
            .Select(row => row.Values
                .FirstOrDefault(value => value.DemandColumnId == column.Id)
                ?.Demand ?? 0)
            .Sum();
    }

    private bool IsShopOpen(DateOnly weekStart, int columnPosition, int hour)
    {
        if (columnPosition < 0 || columnPosition >= DayLabels.Length)
        {
            return false;
        }

        var dayOfWeek = weekStart.AddDays(columnPosition).DayOfWeek;
        var hours = shopHours.For(dayOfWeek);
        var openingMinutes = ToMinutes(hours.OpeningTime);
        var closingMinutes = ToMinutes(hours.ClosingTime);
        var hourMinutes = hour * 60;

        if (closingMinutes <= openingMinutes)
        {
            closingMinutes += 24 * 60;
        }

        if (hourMinutes < openingMinutes)
        {
            hourMinutes += 24 * 60;
        }

        return hourMinutes >= openingMinutes && hourMinutes < closingMinutes;
    }

    private static int ToMinutes(TimeOnly time) => time.Hour * 60 + time.Minute;

    private static string GetColumnLabel(int position)
    {
        return position >= 0 && position < DayLabels.Length
            ? DayLabels[position]
            : $"Other {position - DayLabels.Length + 1}";
    }

    private static int GetDisplayHourOrder(int hour)
    {
        return hour < 6 ? hour + 24 : hour;
    }

    private sealed record ParsedDemand(
        List<ParsedDemandColumn> Columns,
        List<ParsedDemandRow> Rows);

    private sealed record ParsedDemandColumn(int Position, string Label);

    private sealed record ParsedDemandRow(int Hour, List<ParsedDemandValue> Values);

    private sealed record ParsedDemandValue(
        int Position,
        decimal? Deliveries,
        int? Demand);
}
