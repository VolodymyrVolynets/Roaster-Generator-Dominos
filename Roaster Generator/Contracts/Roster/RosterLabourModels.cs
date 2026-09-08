namespace Roaster_Generator.Contracts.Roster;

public sealed class RosterLabourRequest
{
    public DateOnly? WeekStart { get; set; }
    public int? WeekOffset { get; set; }
}

public sealed class RosterLabourResponse
{
    public DateOnly WeekStart { get; init; }
    public Guid? DemandPlanId { get; init; }
    public bool HasDriverRoster { get; init; }
    public decimal? TargetSales { get; init; }
    public RosterLabourTotalsResponse Drivers { get; init; } = new();
    public IReadOnlyList<RosterLabourDayResponse> Days { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed class RosterLabourDayResponse
{
    public DateOnly Date { get; init; }
    public string Label { get; init; } = string.Empty;
    public decimal? TargetSales { get; init; }
    public RosterLabourTotalsResponse Drivers { get; init; } = new();
}

public sealed class RosterLabourTotalsResponse
{
    // A zero-hour saved roster is complete; an absent roster is not.
    public bool IsComplete { get; init; }
    public decimal ScheduledHours { get; init; }
    public decimal LabourCost { get; init; }
    public decimal? LabourPercentage { get; init; }
}
