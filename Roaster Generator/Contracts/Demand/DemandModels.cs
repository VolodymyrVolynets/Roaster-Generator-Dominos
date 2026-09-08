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

    public decimal HourlyRate { get; set; }

    private decimal insideHourlyRate;
    private decimal deliveriesPerDriverHour = 2.7m;
    private decimal pizzasPerInsideHour = 20m;

    public decimal InsideHourlyRate
    {
        get => insideHourlyRate;
        set { insideHourlyRate = value; InsideHourlyRateSpecified = true; }
    }

    public decimal DeliveriesPerDriverHour
    {
        get => deliveriesPerDriverHour;
        set { deliveriesPerDriverHour = value; DeliveriesPerDriverHourSpecified = true; }
    }

    public decimal PizzasPerInsideHour
    {
        get => pizzasPerInsideHour;
        set { pizzasPerInsideHour = value; PizzasPerInsideHourSpecified = true; }
    }

    // Presence is separate from zero/default values so older clients retain saved settings.
    internal bool InsideHourlyRateSpecified { get; private set; }
    internal bool DeliveriesPerDriverHourSpecified { get; private set; }
    internal bool PizzasPerInsideHourSpecified { get; private set; }

    public bool RecalculateDemand { get; set; }

    public List<DemandColumnRequest> Columns { get; set; } = [];

    public List<DemandRowRequest> Rows { get; set; } = [];
}

public sealed class DemandColumnRequest
{
    public int Position { get; set; }

    public string Label { get; set; } = string.Empty;

    public decimal TargetSales { get; set; }
}

public sealed class DemandRowRequest
{
    public int Hour { get; set; }

    public List<DemandValueRequest> Values { get; set; } = [];
}

public sealed class DemandValueRequest
{
    public int Position { get; set; }

    private decimal? deliveries;
    private decimal? pizzas;
    private int? demand;
    private int? insideDemand;

    public decimal? Deliveries
    {
        get => deliveries;
        set { deliveries = value; DeliveriesSpecified = true; }
    }

    public decimal? Pizzas
    {
        get => pizzas;
        set { pizzas = value; PizzasSpecified = true; }
    }

    public int? Demand
    {
        get => demand;
        set { demand = value; DemandSpecified = true; }
    }

    public int? InsideDemand
    {
        get => insideDemand;
        set { insideDemand = value; InsideDemandSpecified = true; }
    }

    internal bool DeliveriesSpecified { get; private set; }
    internal bool PizzasSpecified { get; private set; }
    internal bool DemandSpecified { get; private set; }
    internal bool InsideDemandSpecified { get; private set; }
}

public sealed class DemandPlanSummaryResponse
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public DateOnly WeekStart { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public int RowCount { get; init; }

    public int ColumnCount { get; init; }

    public decimal HourlyRate { get; init; }

    public decimal WeeklyTargetSales { get; init; }
}

public sealed class DemandPlanResponse
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public DateOnly WeekStart { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public decimal HourlyRate { get; init; }

    public decimal InsideHourlyRate { get; init; }

    public decimal DeliveriesPerDriverHour { get; init; }

    public decimal PizzasPerInsideHour { get; init; }

    public int WeeklyDriverHours { get; init; }

    public int WeeklyInsideHours { get; init; }

    public decimal WeeklyDriverLabourCost { get; init; }

    public decimal WeeklyInsideLabourCost { get; init; }

    public decimal WeeklyTargetSales { get; init; }

    public decimal WeeklyLabourCost { get; init; }

    public decimal? WeeklyLabourPercentage { get; init; }

    public List<DemandColumnResponse> Columns { get; init; } = [];

    public List<DemandRowResponse> Rows { get; init; } = [];

    public List<DemandLabourDayResponse> DailyLabour { get; init; } = [];
}

public sealed class DemandColumnResponse
{
    public int Position { get; init; }

    public string Label { get; init; } = string.Empty;

    public int TotalHours { get; init; }

    public int InsideTotalHours { get; init; }

    public decimal TargetSales { get; init; }
}

public sealed class DemandLabourDayResponse
{
    public int Position { get; init; }

    public string Label { get; init; } = string.Empty;

    public decimal TargetSales { get; init; }

    public int RequiredDriverHours { get; init; }

    public int RequiredInsideHours { get; init; }

    public decimal AppliedHourlyRate { get; init; }

    public decimal AppliedInsideHourlyRate { get; init; }

    public decimal DriverLabourCost { get; init; }

    public decimal InsideLabourCost { get; init; }

    public decimal LabourCost { get; init; }

    public decimal? LabourPercentage { get; init; }
}

public sealed class DemandRowResponse
{
    public int Hour { get; init; }

    public List<DemandValueResponse> Values { get; init; } = [];
}

public sealed class DemandValueResponse
{
    public int Position { get; init; }

    public bool IsOpen { get; init; }

    public decimal? Deliveries { get; init; }

    public decimal? Pizzas { get; init; }

    public int? Demand { get; init; }

    public int? InsideDemand { get; init; }
}
