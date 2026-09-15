using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Roaster_Generator.Configuration;
using Roaster_Generator.Contracts.Demand;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Enums;

namespace Roaster_Generator.Services;

public sealed class DemandValidationException(string message) : Exception(message);

public sealed class DemandService(
    AppDbContext db,
    IOptions<ShopHoursOptions> shopHoursOptions,
    RosterInputService rosterInputs)
{
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
            .OrderByDescending(plan => plan.WeekStart)
            .ThenBy(plan => plan.DemandKind)
            .Select(plan => new DemandPlanSummaryResponse
            {
                Id = plan.Id,
                Name = plan.Name,
                WeekStart = plan.WeekStart,
                DemandKind = plan.DemandKind,
                UpdatedAtUtc = plan.UpdatedAtUtc,
                RowCount = plan.Rows.Count,
                ColumnCount = plan.Columns.Count,
                WeeklyTargetSales = plan.Columns.Sum(column => column.TargetSales)
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<DemandPlanResponse?> GetPlanAsync(
        Guid planId,
        CancellationToken cancellationToken)
    {
        var plan = await LoadPlanAsync(planId, cancellationToken);
        return plan is null ? null : await ToResponseAsync(plan, cancellationToken);
    }

    public async Task<DemandPlanResponse> ImportTextAsync(
        DemandImportRequest request,
        CancellationToken cancellationToken)
    {
        EnsureDemandKind(request.DemandKind);
        var parsed = ParseText(request.Content, request.DemandKind);
        return await SaveImportedPlanAsync(request.Name, request.WeekStart, request.DemandKind, parsed, cancellationToken);
    }

    public async Task<DemandPlanResponse> ImportExcelAsync(
        string? name,
        DateOnly weekStart,
        string demandKind,
        Stream stream,
        CancellationToken cancellationToken)
    {
        EnsureDemandKind(demandKind);
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

        var parsed = ParseRows(rows, demandKind);
        var planName = string.IsNullOrWhiteSpace(name) ? "Imported demand" : name.Trim();
        return await SaveImportedPlanAsync(planName, weekStart, demandKind, parsed, cancellationToken);
    }

    public async Task<DemandPlanResponse> UpdateAsync(
        Guid planId,
        DemandPlanUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var plan = await LoadPlanAsync(planId, cancellationToken)
            ?? throw new DemandValidationException("Demand plan not found.");

        EnsureDemandKind(request.DemandKind);
        if (!string.Equals(plan.DemandKind, request.DemandKind, StringComparison.Ordinal))
            throw new DemandValidationException("The selected demand scenario does not match this plan.");

        var existingPositions = plan.Columns.Select(column => column.Position).ToHashSet();
        var requestedPositions = request.Columns.Select(column => column.Position).ToHashSet();

        if (!existingPositions.SetEquals(requestedPositions))
        {
            throw new DemandValidationException("Demand columns can only be changed by importing a new table.");
        }

        var existingHours = plan.Rows.Select(row => row.Hour).ToHashSet();
        var requestedHours = request.Rows.Select(row => row.Hour).ToHashSet();

        if (!existingHours.SetEquals(requestedHours))
        {
            throw new DemandValidationException("Demand hours can only be changed by importing a new table.");
        }

        var otherPlan = await db.DemandPlans
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.WeekStart == request.WeekStart &&
                item.DemandKind == request.DemandKind && item.Id != planId, cancellationToken);

        if (otherPlan is not null)
        {
            throw new DemandValidationException(
                "A demand plan already exists for this week. Edit that plan instead.");
        }

        var positionsByColumnId = plan.Columns.ToDictionary(column => column.Id, column => column.Position);
        var existingValues = plan.Rows
            .SelectMany(row => row.Values.Select(value =>
                (row.Hour, Position: positionsByColumnId[value.DemandColumnId], Value: value)))
            .ToDictionary(value => (value.Hour, value.Position), value => value.Value);
        var settings = SettingsForUpdate(plan, request);

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
                        if (!existingValues.TryGetValue((row.Hour, value.Position), out var previous))
                        {
                            throw new DemandValidationException(
                                $"The demand value for hour {row.Hour:00} and position {value.Position} does not exist.");
                        }

                        return ApplyValueEdit(previous, value, settings);
                    }).ToList()))
                .ToList());

        parsed = NormalizeAndValidateDemand(parsed, request.WeekStart, request.DemandKind);
        foreach (var parsedRow in parsed.Rows)
        {
            var row = plan.Rows.Single(item => item.Hour == parsedRow.Hour);
            var valuesByPosition = row.Values.ToDictionary(
                value => positionsByColumnId[value.DemandColumnId]);

            foreach (var parsedValue in parsedRow.Values)
            {
                var value = valuesByPosition[parsedValue.Position];
                if (request.DemandKind == DemandKinds.Inside)
                {
                    value.Pizzas = parsedValue.Pizzas;
                    value.InsideDemand = parsedValue.InsideDemand;
                }
                else
                {
                    value.Deliveries = parsedValue.Deliveries;
                    value.Demand = parsedValue.Demand;
                }
            }
        }

        plan.Name = request.Name.Trim();
        plan.WeekStart = request.WeekStart;
        plan.DemandKind = request.DemandKind;
        plan.DeliveriesPerDriverHour = settings.DeliveriesPerDriverHour;
        plan.PizzasPerInsideHour = settings.PizzasPerInsideHour;
        foreach (var columnRequest in request.Columns)
        {
            plan.Columns.Single(column => column.Position == columnRequest.Position).TargetSales =
                columnRequest.TargetSales;
        }
        plan.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return await ToResponseAsync(await LoadPlanAsync(plan.Id, cancellationToken) ?? plan, cancellationToken);
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
        string demandKind,
        ParsedDemand parsed,
        CancellationToken cancellationToken)
    {
        EnsureDemandKind(demandKind);
        var existingPlan = await db.DemandPlans
            .Include(plan => plan.Columns)
            .Include(plan => plan.Rows)
            .ThenInclude(row => row.Values)
            .SingleOrDefaultAsync(plan => plan.WeekStart == weekStart && plan.DemandKind == demandKind,
                cancellationToken);

        parsed = Recalculate(parsed, demandKind,
            demandKind == DemandKinds.Inside
                ? existingPlan?.PizzasPerInsideHour ?? DemandStaffing.DefaultPizzasPerInsideHour
                : existingPlan?.DeliveriesPerDriverHour ?? DemandStaffing.DefaultDeliveriesPerDriverHour);
        parsed = NormalizeAndValidateDemand(parsed, weekStart, demandKind);

        if (existingPlan is null)
        {
            var plan = CreatePlan(name, weekStart, demandKind, parsed);
            db.DemandPlans.Add(plan);
            await db.SaveChangesAsync(cancellationToken);
            return await ToResponseAsync(await LoadPlanAsync(plan.Id, cancellationToken) ?? plan, cancellationToken);
        }

        await UpdatePlanContentsAsync(existingPlan, name.Trim(), weekStart, demandKind, parsed, cancellationToken);
        db.ChangeTracker.Clear();
        return await ToResponseAsync(await LoadPlanAsync(existingPlan.Id, cancellationToken) ?? existingPlan,
            cancellationToken);
    }

    private async Task UpdatePlanContentsAsync(
        DemandPlan plan,
        string name,
        DateOnly weekStart,
        string demandKind,
        ParsedDemand parsed,
        CancellationToken cancellationToken)
    {
        plan.Name = name;
        plan.WeekStart = weekStart;
        plan.DemandKind = demandKind;
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

                if (demandKind == DemandKinds.Inside)
                {
                    value.Pizzas = parsedValue.Pizzas;
                    value.InsideDemand = parsedValue.InsideDemand;
                }
                else
                {
                    value.Deliveries = parsedValue.Deliveries;
                    value.Demand = parsedValue.Demand;
                }
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

    private static DemandPlan CreatePlan(string name, DateOnly weekStart, string demandKind, ParsedDemand parsed)
    {
        var now = DateTimeOffset.UtcNow;
        var plan = new DemandPlan
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            WeekStart = weekStart,
            DemandKind = demandKind,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        AddParsedContents(plan, parsed, demandKind);
        return plan;
    }

    private static void AddParsedContents(DemandPlan plan, ParsedDemand parsed, string demandKind)
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
                    Deliveries = demandKind == DemandKinds.Outside ? parsedValue.Deliveries : null,
                    Demand = demandKind == DemandKinds.Outside ? parsedValue.Demand : null,
                    Pizzas = demandKind == DemandKinds.Inside ? parsedValue.Pizzas : null,
                    InsideDemand = demandKind == DemandKinds.Inside ? parsedValue.InsideDemand : null
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

    internal DemandPlanResponse ToResponse(DemandPlan plan)
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
        var dailyStaffing = columns
            .Select(column => new DemandStaffingDayResponse
            {
                Position = column.Position,
                Label = column.Label,
                TargetSales = column.TargetSales,
                RequiredStaffHours = column.TotalHours
            })
            .ToList();

        return new DemandPlanResponse
        {
            Id = plan.Id,
            Name = plan.Name,
            WeekStart = plan.WeekStart,
            DemandKind = plan.DemandKind,
            UpdatedAtUtc = plan.UpdatedAtUtc,
            DeliveriesPerDriverHour = plan.DeliveriesPerDriverHour,
            PizzasPerInsideHour = plan.PizzasPerInsideHour,
            WeeklyStaffHours = dailyStaffing.Sum(item => item.RequiredStaffHours),
            WeeklyTargetSales = dailyStaffing.Sum(item => item.TargetSales),
            Columns = columns,
            DailyStaffing = dailyStaffing,
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
                            IsOpen = IsShopOpen(plan.WeekStart, columnsById[value.DemandColumnId].Position, row.Hour),
                            Deliveries = plan.DemandKind == DemandKinds.Outside ? value.Deliveries : null,
                            Demand = plan.DemandKind == DemandKinds.Outside ? value.Demand : null,
                            Pizzas = plan.DemandKind == DemandKinds.Inside ? value.Pizzas : null,
                            InsideDemand = plan.DemandKind == DemandKinds.Inside ? value.InsideDemand : null
                        })
                        .ToList()
                })
                .ToList()
        };
    }

    private async Task<DemandPlanResponse> ToResponseAsync(DemandPlan plan, CancellationToken cancellationToken)
    {
        var response = ToResponse(plan);
        if (plan.DemandKind != DemandKinds.Outside)
        {
            return response;
        }

        try
        {
            var loaded = await rosterInputs.LoadAsync(plan.WeekStart, cancellationToken);
            var drivers = loaded.Input.Employees.Select(employee =>
            {
                var allocation = loaded.Input.ExpectedHoursByEmployee?.GetValueOrDefault(employee.Id);
                var approximateHours = allocation?.ExpectedHours ?? 0;
                return new DemandLabourDriverResponse
                {
                    EmployeeId = employee.Id,
                    EmployeeName = $"{employee.FirstName} {employee.LastName}".Trim(),
                    ApproximateHours = approximateHours,
                    CapacityHours = allocation?.CapacityHours ?? 0,
                    HourlyRate = employee.HourlyRate,
                    ApproximateBaseCost = Money((decimal)approximateHours * employee.HourlyRate)
                };
            }).OrderBy(item => item.EmployeeName).ThenBy(item => item.EmployeeId).ToList();
            var totalApproximateHours = drivers.Sum(item => item.ApproximateHours);
            var weightedCost = drivers.Sum(item => (decimal)item.ApproximateHours * item.HourlyRate);
            var totalDemandHours = loaded.Input.Demand.Sum(item => (double)item.RequiredDrivers);
            response.LabourEstimate = new DemandLabourEstimateResponse
            {
                IsAvailable = totalDemandHours == 0 || totalApproximateHours > 0,
                Message = totalDemandHours == 0
                    ? "Demand is zero, so estimated driver labour is €0."
                    : totalApproximateHours > 0
                        ? "Pay is weighted by each driver's automatically calculated approximate hours for this week."
                        : "No useful driver availability overlaps this week's demand, so weighted labour cannot be estimated.",
                TotalDemandHours = totalDemandHours,
                TotalApproximateHours = totalApproximateHours,
                UnallocatedDemandHours = Math.Max(0, totalDemandHours - totalApproximateHours),
                WeightedAverageHourlyRate = totalApproximateHours > 0
                    ? weightedCost / (decimal)totalApproximateHours
                    : totalDemandHours == 0 ? 0 : null,
                ApproximateBaseLabourCost = totalApproximateHours > 0
                    ? Money(weightedCost)
                    : totalDemandHours == 0 ? 0 : null,
                Drivers = drivers
            };
        }
        catch (RosterInputException exception)
        {
            response.LabourEstimate = new DemandLabourEstimateResponse
            {
                Message = exception.Message
            };
        }

        return response;
    }

    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    internal static ParsedDemand ParseText(string content, string demandKind = DemandKinds.Outside)
    {
        var rows = content
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(SplitTextRow)
            .Where(row => row.Length > 0)
            .ToList();

        return ParseRows(rows, demandKind);
    }

    private static string[] SplitTextRow(string row)
    {
        // Trailing tab cells are meaningful missing workload values.
        var trimmed = row.Trim(' ', '\r', '\n');

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

    private static ParsedDemand ParseRows(IReadOnlyList<string[]> rows, string demandKind)
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

        var maxValueCells = Math.Max(dataRows.Max(item => Math.Max(0, item.Cells.Length - 1)),
            rows.Where(row => row.Any(cell => cell.Contains("pizza", StringComparison.OrdinalIgnoreCase) ||
                cell.Contains("deliver", StringComparison.OrdinalIgnoreCase)))
                .Select(row => Math.Max(0, row.Length - 1)).DefaultIfEmpty(0).Max());
        var activeIndexes = Enumerable.Range(0, maxValueCells)
            .Where(index => maxValueCells == 14 || dataRows.Any(item =>
                item.Cells.Length > index + 1 &&
                !string.IsNullOrWhiteSpace(item.Cells[index + 1])) || rows.Any(row =>
                row.Length > index + 1 &&
                (row[index + 1].Contains("pizza", StringComparison.OrdinalIgnoreCase) ||
                 row[index + 1].Contains("deliver", StringComparison.OrdinalIgnoreCase))))
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
                var label = $"{GetColumnLabel(position)} {dataRow.Hour:00}:00";
                var workload = ParseDecimal(GetCell(dataRow.Cells, activeIndexes, position * 2 +
                    (demandKind == DemandKinds.Inside ? 0 : 1)),
                    $"{label} {(demandKind == DemandKinds.Inside ? "pizzas" : "deliveries")}");

                values.Add(demandKind == DemandKinds.Inside
                    ? new ParsedDemandValue(position, null, null, workload,
                        DemandStaffing.Inside(workload, DemandStaffing.DefaultPizzasPerInsideHour))
                    : new ParsedDemandValue(position, workload,
                        DemandStaffing.Drivers(workload, DemandStaffing.DefaultDeliveriesPerDriverHour), null, null));
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

    private static decimal? ParseDecimal(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim() == "#")
        {
            return null;
        }

        if (!decimal.TryParse(
            value.Trim(),
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var result) || result is < 0m or > DemandStaffing.MaximumWorkload)
            throw new DemandValidationException($"{label}: enter a number between 0 and 1000000, or leave it blank if unknown.");
        return result;
    }

    internal static DemandEditSettings SettingsForUpdate(DemandPlan plan, DemandPlanUpdateRequest request)
    {
        var deliveries = request.DeliveriesPerDriverHourSpecified
            ? request.DeliveriesPerDriverHour : plan.DeliveriesPerDriverHour;
        var pizzas = request.PizzasPerInsideHourSpecified
            ? request.PizzasPerInsideHour : plan.PizzasPerInsideHour;
        var recalculate = request.RecalculateDemand ||
            (plan.DemandKind == DemandKinds.Inside
                ? pizzas != plan.PizzasPerInsideHour
                : deliveries != plan.DeliveriesPerDriverHour);
        return new(deliveries, recalculate, pizzas, plan.DemandKind);
    }

    internal static ParsedDemandValue ApplyValueEdit(DemandValue previous, DemandValueRequest value,
        DemandEditSettings settings)
    {
        var deliveries = value.DeliveriesSpecified ? value.Deliveries : previous.Deliveries;
        var pizzas = value.PizzasSpecified ? value.Pizzas : previous.Pizzas;
        var driverDemand = settings.DemandKind == DemandKinds.Outside && (settings.Recalculate ||
            (previous.Deliveries != deliveries && !value.DemandSpecified))
            ? DemandStaffing.Drivers(deliveries, settings.DeliveriesPerDriverHour)
            : value.DemandSpecified ? value.Demand : previous.Demand;
        var insideDemand = settings.DemandKind == DemandKinds.Inside && (settings.Recalculate ||
            (previous.Pizzas != pizzas && !value.InsideDemandSpecified))
            ? DemandStaffing.Inside(pizzas, settings.PizzasPerInsideHour)
            : value.InsideDemandSpecified ? value.InsideDemand : previous.InsideDemand;
        return new(value.Position, deliveries, driverDemand, pizzas, insideDemand);
    }

    internal static ParsedDemand Recalculate(ParsedDemand parsed, string demandKind, decimal productivity) =>
        new(parsed.Columns, parsed.Rows.Select(row => new ParsedDemandRow(row.Hour,
            row.Values.Select(value => value with
            {
                Demand = demandKind == DemandKinds.Outside
                    ? DemandStaffing.Drivers(value.Deliveries, productivity) : value.Demand,
                InsideDemand = demandKind == DemandKinds.Inside
                    ? DemandStaffing.Inside(value.Pizzas, productivity) : value.InsideDemand
            }).ToList())).ToList());

    internal static ParsedDemand Recalculate(ParsedDemand parsed, decimal deliveriesPerDriverHour) =>
        Recalculate(parsed, DemandKinds.Outside, deliveriesPerDriverHour);

    internal ParsedDemand NormalizeAndValidateDemand(ParsedDemand parsed, DateOnly weekStart,
        string demandKind = DemandKinds.Outside)
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
                            item => new ParsedDemandValue(item.Position, null, null, null, null));
                    valuesByHour[hour] = values;
                }

                if (!values.TryGetValue(column.Position, out var currentValue) ||
                    !HasInput(currentValue))
                {
                    values[column.Position] = new ParsedDemandValue(
                        column.Position,
                        lastProvided.Value.Deliveries,
                        lastProvided.Value.Demand,
                        lastProvided.Value.Pizzas,
                        lastProvided.Value.InsideDemand);
                }
            }
        }

        var normalizedRows = valuesByHour
            .OrderBy(item => GetDisplayHourOrder(item.Key))
            .Select(item => new ParsedDemandRow(
                item.Key,
                parsed.Columns
                    .OrderBy(column => column.Position)
                    .Select(column => item.Value.TryGetValue(column.Position, out var existingValue)
                            ? existingValue
                            : new ParsedDemandValue(column.Position, null, null, null, null))
                    .ToList()))
            .ToList();
        var normalized = new ParsedDemand(parsed.Columns, normalizedRows);

        return normalized;
    }

    private static bool HasInput(ParsedDemandValue value) =>
        value.Deliveries is not null || value.Demand is not null ||
        value.Pizzas is not null || value.InsideDemand is not null;

    private static IEnumerable<int> GetDisplayHours() =>
        Enumerable.Range(6, 18).Concat(Enumerable.Range(0, 6));

    private int CalculateTotalHours(DemandPlan plan, DemandColumn column)
    {
        return plan.Rows
            .Where(row => IsShopOpen(plan.WeekStart, column.Position, row.Hour))
            .Select(row => row.Values.FirstOrDefault(value => value.DemandColumnId == column.Id))
            .Sum(value => plan.DemandKind == DemandKinds.Inside
                ? value?.InsideDemand ?? 0 : value?.Demand ?? 0);
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

    private static void EnsureDemandKind(string? demandKind)
    {
        if (!DemandKinds.IsValid(demandKind))
            throw new DemandValidationException("Demand kind must be outside or inside.");
    }

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

    internal sealed record ParsedDemand(
        List<ParsedDemandColumn> Columns,
        List<ParsedDemandRow> Rows);

    internal sealed record DemandEditSettings(decimal DeliveriesPerDriverHour,
        bool Recalculate, decimal PizzasPerInsideHour = DemandStaffing.DefaultPizzasPerInsideHour,
        string DemandKind = DemandKinds.Outside);

    internal sealed record ParsedDemandColumn(int Position, string Label);

    internal sealed record ParsedDemandRow(int Hour, List<ParsedDemandValue> Values);

    internal sealed record ParsedDemandValue(
        int Position,
        decimal? Deliveries,
        int? Demand,
        decimal? Pizzas,
        int? InsideDemand);
}
