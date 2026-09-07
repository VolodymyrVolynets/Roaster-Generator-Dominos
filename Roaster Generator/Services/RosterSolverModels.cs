using Roaster_Generator.Entities;

namespace Roaster_Generator.Services;

/// <summary>All times use local shop wall-clock hours, with overnight hours represented as 24, 25, etc.</summary>
public sealed record RosterSolverInput(
    DateOnly WeekStart,
    IReadOnlyList<Employee> Employees,
    IReadOnlyList<Shift> Availability,
    IReadOnlyList<RosterSolverDemand> Demand,
    IReadOnlyList<RosterSolverBoundaryShift> BoundaryShifts,
    RosterSolverOptions Options,
    IReadOnlyList<RosterSolverHistory>? History = null);

public sealed record RosterSolverHistory(Guid EmployeeId, DateOnly WeekStart, int ScheduledHours, int TargetHours);

public sealed record RosterSolverDemand(DateOnly Date, int Hour, int RequiredDrivers);

public sealed record RosterSolverBoundaryShift(Guid EmployeeId, DateTime Start, DateTime Finish);

public sealed record RosterSolverShift(Guid EmployeeId, DateOnly Date, int StartHour, int FinishHour)
{
    public int DurationHours => FinishHour - StartHour;
}

public sealed record RosterSolverProgress(string Stage, int Progress, string Message);

public sealed class RosterSolverOptions
{
    public int TargetHoursWeight { get; init; } = 100;
    public int HistoryFairnessWeight { get; init; } = 100;
    public int FairnessSpreadWeight { get; init; } = 1000;
    public int LongShiftBonus { get; init; } = 25;
    public int ShortShiftPenalty { get; init; } = 10;
    public int DailyShiftCountPenalty { get; init; } = 25;
    public int ShortBreakPenalty { get; init; } = 100;
    public int MinimumRestHours { get; init; } = 8;
    public int PreferredRestHours { get; init; } = 12;
    public int MaxSolveSeconds { get; init; } = 20;
}

public sealed class RosterSolverResult
{
    public string Status { get; init; } = "invalid";
    public bool Success => Status is "optimal" or "feasible";
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<RosterSolverShift> Shifts { get; init; } = [];
    public IReadOnlyList<string> Diagnostics { get; init; } = [];
    public int TotalDemandHours { get; init; }
    public int TotalScheduledHours { get; init; }
    public double WallTimeSeconds { get; init; }
    public int CandidateCount { get; init; }
    public double? ObjectiveValue { get; init; }
    public double TargetUtilizationPercent { get; init; }
}
