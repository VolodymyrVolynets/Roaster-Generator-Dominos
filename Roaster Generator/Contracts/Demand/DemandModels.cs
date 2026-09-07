namespace Roaster_Generator.Contracts.Demand;

public sealed class DemandImportRequest
{
    public string Name { get; set; } = string.Empty;

    public DateOnly WeekStart { get; set; }

    public string Content { get; set; } = string.Empty;
}

public sealed class DemandPlanUpdateRequest
{
    public string Name { get; set; } = string.Empty;

    public DateOnly WeekStart { get; set; }

    public List<DemandColumnRequest> Columns { get; set; } = [];

    public List<DemandRowRequest> Rows { get; set; } = [];
}

public sealed class DemandColumnRequest
{
    public int Position { get; set; }

    public string Label { get; set; } = string.Empty;
}

public sealed class DemandRowRequest
{
    public int Hour { get; set; }

    public List<DemandValueRequest> Values { get; set; } = [];
}

public sealed class DemandValueRequest
{
    public int Position { get; set; }

    public int? Demand { get; set; }
}

public sealed class DemandPlanSummaryResponse
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public DateOnly WeekStart { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public int RowCount { get; init; }

    public int ColumnCount { get; init; }
}

public sealed class DemandPlanResponse
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public DateOnly WeekStart { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public List<DemandColumnResponse> Columns { get; init; } = [];

    public List<DemandRowResponse> Rows { get; init; } = [];
}

public sealed class DemandColumnResponse
{
    public int Position { get; init; }

    public string Label { get; init; } = string.Empty;

    public int TotalHours { get; init; }
}

public sealed class DemandRowResponse
{
    public int Hour { get; init; }

    public List<DemandValueResponse> Values { get; init; } = [];
}

public sealed class DemandValueResponse
{
    public int Position { get; init; }

    public decimal? Deliveries { get; init; }

    public int? Demand { get; init; }
}
