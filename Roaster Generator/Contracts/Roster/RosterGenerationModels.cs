namespace Roaster_Generator.Contracts.Roster;

public sealed class RosterTimerStartResponse
{
    public string RosterKind { get; init; } = "drivers";
    public DateTimeOffset TimestampUtc { get; init; }

    public long Sequence { get; init; }

    public Guid JobId { get; init; }

    public int WeekOffset { get; init; }

    public DateOnly WeekStart { get; init; }

    public string Status { get; init; } = string.Empty;

    public string Stage { get; init; } = string.Empty;

    public int Progress { get; init; }

    public string Message { get; init; } = string.Empty;
}

public sealed class RosterTimerCancelRequest
{
    public int WeekOffset { get; init; }

    public Guid? JobId { get; init; }
}

public sealed class RosterPlanResponse
{
    public string RosterKind { get; init; } = "drivers";
    public Guid Id { get; init; }

    public DateOnly WeekStart { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public string? DemandFingerprint { get; init; }

    public string? AvailabilityFingerprint { get; init; }

    public string FreshnessStatus { get; set; } = "unknown";

    public IReadOnlyList<string> FreshnessWarnings { get; set; } = [];

    public IReadOnlyList<RosterCoverageResponse>? CurrentCoverage { get; set; }

    public int? CurrentDemandHours { get; set; }

    public double? CurrentCoveragePercent { get; set; }

    public int TotalDemandHours { get; init; }

    public int TotalScheduledHours { get; init; }

    // Derive from actual shifts so historical snapshots also expose this metric.
    public double AverageHoursPerShift
    {
        get
        {
            var count = Employees.Sum(employee => employee.Shifts.Count);
            return count == 0 ? 0 : Math.Round(
                Employees.Sum(employee => employee.Shifts.Sum(shift => shift.DurationHours)) / (double)count, 2);
        }
    }

    public double? CoveragePercent { get; init; }

    public string SolverStatus { get; init; } = "legacy";

    public bool IsOptimal { get; init; }

    public double SolveSeconds { get; init; }

    public double? MinimumRestHours { get; init; }

    public double FairnessSpreadPercentagePoints { get; init; }

    public double HistoricalFairnessSpreadPercentagePoints { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public RosterSettingsRequest? Settings { get; init; }

    public IReadOnlyList<RosterCoverageResponse> Coverage { get; init; } = [];

    public IReadOnlyList<RosterEmployeeResponse> Employees { get; init; } = [];
}

public sealed class RosterPlanUpdateRequest
{
    public string RosterKind { get; set; } = "drivers";
    public DateOnly WeekStart { get; init; }

    public List<RosterShiftUpdateRequest> Shifts { get; init; } = [];
}

public sealed class RosterShiftUpdateRequest
{
    public Guid EmployeeId { get; init; }

    public DateOnly Date { get; init; }

    /// <summary>Absolute business-day hour, 0–47. Starts after midnight are rejected by validation.</summary>
    public int StartHour { get; init; }

    /// <summary>Absolute business-day hour, 0–48. Values above 24 represent an overnight finish.</summary>
    public int FinishHour { get; init; }
}

public sealed class RosterWeekSummaryResponse
{
    public string RosterKind { get; init; } = "drivers";
    public DateOnly WeekStart { get; init; }

    public bool DemandPlanExists { get; init; }

    public int RequiredDriverHours { get; init; }

    public int EnteredAvailabilityHours { get; init; }

    public int DriversWithoutAvailability { get; init; }
}

public sealed class RosterEmployeeResponse
{
    public IReadOnlyList<string> Roles { get; init; } = [];
    public Guid EmployeeId { get; init; }

    public string EmployeeName { get; init; } = string.Empty;

    public int TargetHours { get; init; }

    public int ScheduledHours { get; init; }

    public double AverageHoursPerShift => Shifts.Count == 0 ? 0
        : Math.Round(Shifts.Sum(shift => shift.DurationHours) / (double)Shifts.Count, 2);

    public double? TargetPercentage { get; init; }

    public int PreviousScheduledHours { get; init; }

    public int? PreviousShiftCount { get; init; }

    public double? PreviousAverageHoursPerShift { get; init; }

    public int PreviousTargetHours { get; init; }

    public double? PreviousTargetPercentage { get; init; }

    public int HistoryWeeks { get; init; }

    public double? BalancedTargetHours { get; init; }

    public double? CumulativeTargetPercentage { get; init; }

    public IReadOnlyList<RosterShiftResponse> Shifts { get; init; } = [];
}

public sealed class RosterShiftResponse
{
    public DateOnly Date { get; init; }

    public string StartTime { get; init; } = string.Empty;

    public string FinishTime { get; init; } = string.Empty;

    public int DurationHours { get; init; }

    public int StartDayOffset { get; init; }

    public int FinishDayOffset { get; init; }
}

public sealed class RosterTimerProgressResponse
{
    public string RosterKind { get; init; } = "drivers";
    public Guid JobId { get; init; }

    public int WeekOffset { get; init; }

    public DateOnly WeekStart { get; init; }

    public string Status { get; init; } = string.Empty;

    public string Stage { get; init; } = string.Empty;

    public int Progress { get; init; }

    public string Message { get; init; } = string.Empty;

    public DateTimeOffset TimestampUtc { get; init; }

    public Guid? RosterPlanId { get; init; }

    public int? TotalScheduledHours { get; init; }

    public long Sequence { get; init; }

    public string Severity { get; init; } = "info";

    public IReadOnlyList<string> Diagnostics { get; init; } = [];
}

public sealed class RosterCoverageResponse
{
    public DateOnly Date { get; init; }
    public string StartTime { get; init; } = string.Empty;
    public int StartDayOffset { get; init; }
    public int Required { get; init; }
    public int Scheduled { get; init; }
}

public sealed class RosterSettingsRequest
{
    public int TargetHoursWeight { get; set; } = 100;
    public int HistoryFairnessWeight { get; set; } = 100;
    public int HistoryShiftLengthWeight { get; set; } = 100;
    public int FairnessSpreadWeight { get; set; } = 1000;
    public int LongShiftBonus { get; set; } = 25;
    public int ShortShiftPenalty { get; set; } = 10;
    public int DailyShiftCountPenalty { get; set; } = 25;
    public int ShortBreakPenalty { get; set; } = 100;
    public int MinimumRestHours { get; set; } = 8;
    public int PreferredRestHours { get; set; } = 12;
    public int LatestShiftStartHour { get; set; } = 20;
    public int MaxSolveSeconds { get; set; } = 20;
}
