namespace Roaster_Generator.Entities;

public sealed class DemandPlan
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateOnly WeekStart { get; set; }

    public decimal DeliveriesPerDriverHour { get; set; } = 2.7m;

    public decimal PizzasPerInsideHour { get; set; } = 20m;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ICollection<DemandColumn> Columns { get; set; } = new List<DemandColumn>();

    public ICollection<DemandRow> Rows { get; set; } = new List<DemandRow>();
}

public sealed class DemandColumn
{
    public Guid Id { get; set; }

    public Guid DemandPlanId { get; set; }

    public int Position { get; set; }

    public string Label { get; set; } = string.Empty;

    // Sales target for this day. Keeping this on the day column lets the admin
    // compare labour percentage for each day as well as for the whole week.
    public decimal TargetSales { get; set; }

    public DemandPlan DemandPlan { get; set; } = null!;

    public ICollection<DemandValue> Values { get; set; } = new List<DemandValue>();
}

public sealed class DemandRow
{
    public Guid Id { get; set; }

    public Guid DemandPlanId { get; set; }

    public int Hour { get; set; }

    public DemandPlan DemandPlan { get; set; } = null!;

    public ICollection<DemandValue> Values { get; set; } = new List<DemandValue>();
}

public sealed class DemandValue
{
    public Guid Id { get; set; }

    public Guid DemandRowId { get; set; }

    public Guid DemandColumnId { get; set; }

    public decimal? Deliveries { get; set; }

    public decimal? Pizzas { get; set; }

    public int? Demand { get; set; }

    public int? InsideDemand { get; set; }

    public DemandRow Row { get; set; } = null!;

    public DemandColumn Column { get; set; } = null!;
}

public sealed class RosterPlan
{
    public Guid Id { get; set; }

    public string RosterKind { get; set; } = "drivers";

    public DateOnly WeekStart { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    // Immutable generation inputs/metrics and employee names/targets for historical display.
    public string? SnapshotJson { get; set; }

    public ICollection<RosterShift> Shifts { get; set; } = new List<RosterShift>();
}

public sealed class RosterShift
{
    public Guid Id { get; set; }

    public Guid RosterPlanId { get; set; }

    public Guid EmployeeId { get; set; }

    public DateOnly Date { get; set; }

    public TimeOnly StartTime { get; set; }

    public TimeOnly FinishTime { get; set; }

    public RosterPlan RosterPlan { get; set; } = null!;

    public Employee Employee { get; set; } = null!;
}
