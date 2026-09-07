using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Roaster_Generator.Contracts.Demand;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;

namespace Roaster_Generator.Services;

public sealed class DemandValidationException(string message) : Exception(message);

public sealed class DemandService(AppDbContext db)
{
    private const decimal DeliveriesPerEmployee = 2.7m;

    public async Task<IReadOnlyList<DemandPlanSummaryResponse>> GetPlansAsync(
        CancellationToken cancellationToken)
    {
        return await db.DemandPlans
            .AsNoTracking()
            .OrderByDescending(plan => plan.WeekStart)
            .Select(plan => new DemandPlanSummaryResponse
            {
                Id = plan.Id,
                Name = plan.Name,
                WeekStart = plan.WeekStart,
                UpdatedAtUtc = plan.UpdatedAtUtc,
                RowCount = plan.Rows.Count,
                ColumnCount = plan.Columns.Count
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

        if (request.Rows.GroupBy(row => row.Hour).Any(group => group.Count() > 1))
        {
            throw new DemandValidationException("Demand hours must be unique.");
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

        var parsed = new ParsedDemand(
            request.Columns
                .OrderBy(column => column.Position)
                .Select(column => new ParsedDemandColumn(column.Position, column.Label.Trim()))
                .ToList(),
            request.Rows
                .OrderBy(row => row.Hour)
                .Select(row => new ParsedDemandRow(
                    row.Hour,
                    row.Values.Select(value => new ParsedDemandValue(
                        value.Position,
                        value.Pizzas,
                        value.Deliveries,
                        CalculateDemand(value.Deliveries))).ToList()))
                .ToList());

        await ReplacePlanContentsAsync(plan, request.Name.Trim(), request.WeekStart, parsed, cancellationToken);
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
        var existingPlan = await db.DemandPlans
            .Include(plan => plan.Columns)
            .Include(plan => plan.Rows)
            .ThenInclude(row => row.Values)
            .SingleOrDefaultAsync(plan => plan.WeekStart == weekStart, cancellationToken);

        if (existingPlan is null)
        {
            var plan = CreatePlan(name, weekStart, parsed);
            db.DemandPlans.Add(plan);
            await db.SaveChangesAsync(cancellationToken);
            return ToResponse(await LoadPlanAsync(plan.Id, cancellationToken) ?? plan);
        }

        await ReplacePlanContentsAsync(existingPlan, name.Trim(), weekStart, parsed, cancellationToken);
        return ToResponse(await LoadPlanAsync(existingPlan.Id, cancellationToken) ?? existingPlan);
    }

    private async Task ReplacePlanContentsAsync(
        DemandPlan plan,
        string name,
        DateOnly weekStart,
        ParsedDemand parsed,
        CancellationToken cancellationToken)
    {
        db.DemandValues.RemoveRange(plan.Rows.SelectMany(row => row.Values));
        db.DemandRows.RemoveRange(plan.Rows);
        db.DemandColumns.RemoveRange(plan.Columns);
        await db.SaveChangesAsync(cancellationToken);

        plan.Name = name;
        plan.WeekStart = weekStart;
        plan.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddParsedContents(plan, parsed);
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
                Label = string.IsNullOrWhiteSpace(parsedColumn.Label)
                    ? $"Column {parsedColumn.Position + 1}"
                    : parsedColumn.Label
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
                    Pizzas = parsedValue.Pizzas,
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

    private static DemandPlanResponse ToResponse(DemandPlan plan)
    {
        var columns = plan.Columns
            .OrderBy(column => column.Position)
            .Select(column => new DemandColumnResponse
            {
                Position = column.Position,
                Label = column.Label
            })
            .ToList();
        var columnsById = plan.Columns.ToDictionary(column => column.Id);

        return new DemandPlanResponse
        {
            Id = plan.Id,
            Name = plan.Name,
            WeekStart = plan.WeekStart,
            UpdatedAtUtc = plan.UpdatedAtUtc,
            Columns = columns,
            Rows = plan.Rows
                .OrderBy(row => row.Hour)
                .Select(row => new DemandRowResponse
                {
                    Hour = row.Hour,
                    Values = row.Values
                        .Where(value => columnsById.ContainsKey(value.DemandColumnId))
                        .OrderBy(value => columnsById[value.DemandColumnId].Position)
                        .Select(value => new DemandValueResponse
                        {
                            Position = columnsById[value.DemandColumnId].Position,
                            Pizzas = value.Pizzas,
                            Deliveries = value.Deliveries,
                            Demand = value.Demand
                        })
                        .ToList()
                })
                .ToList()
        };
    }

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

        var columns = Enumerable.Range(0, (activeIndexes.Count + 1) / 2)
            .Select(position => new ParsedDemandColumn(position, $"Column {position + 1}"))
            .ToList();

        var parsedRows = new List<ParsedDemandRow>();
        var duplicateHours = dataRows.GroupBy(item => item.Hour).FirstOrDefault(group => group.Count() > 1);

        if (duplicateHours is not null)
        {
            throw new DemandValidationException(
                $"The hour {duplicateHours.Key:00} appears more than once.");
        }

        foreach (var dataRow in dataRows.OrderBy(item => item.Hour))
        {
            var values = new List<ParsedDemandValue>();

            for (var position = 0; position < columns.Count; position++)
            {
                var pizzas = ParseDecimal(GetCell(dataRow.Cells, activeIndexes, position * 2));
                var deliveries = ParseDecimal(GetCell(dataRow.Cells, activeIndexes, position * 2 + 1));

                values.Add(new ParsedDemandValue(
                    position,
                    pizzas,
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

    private sealed record ParsedDemand(
        List<ParsedDemandColumn> Columns,
        List<ParsedDemandRow> Rows);

    private sealed record ParsedDemandColumn(int Position, string Label);

    private sealed record ParsedDemandRow(int Hour, List<ParsedDemandValue> Values);

    private sealed record ParsedDemandValue(
        int Position,
        decimal? Pizzas,
        decimal? Deliveries,
        int? Demand);
}
