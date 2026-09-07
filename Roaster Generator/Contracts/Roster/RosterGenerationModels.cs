namespace Roaster_Generator.Contracts.Roster;

public sealed class RosterGenerationStartResponse
{
    public Guid JobId { get; init; }

    public int WeekOffset { get; init; }

    public DateOnly WeekStart { get; init; }

    public string Status { get; init; } = string.Empty;

    public int Progress { get; init; }

    public string Message { get; init; } = string.Empty;
}

public sealed class RosterGenerationSettingsRequest
{
    public int TargetHoursWeight { get; set; }

    public int LongShiftBonus { get; set; }

    public int ShortShiftPenalty { get; set; }

    public int LateFinishPenalty { get; set; }

    public int EarlyStartPenalty { get; set; }
}

public sealed class RosterGenerationSettingsResponse
{
    public int TargetHoursWeight { get; init; }

    public int LongShiftBonus { get; init; }

    public int ShortShiftPenalty { get; init; }

    public int LateFinishPenalty { get; init; }

    public int EarlyStartPenalty { get; init; }
}

public sealed class RosterPlanResponse
{
    public Guid Id { get; init; }

    public DateOnly WeekStart { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public int TotalDemandHours { get; init; }

    public int TotalScheduledHours { get; init; }

    public IReadOnlyList<RosterEmployeeResponse> Employees { get; init; } = [];
}

public sealed class RosterWeekSummaryResponse
{
    public DateOnly WeekStart { get; init; }

    public bool DemandPlanExists { get; init; }

    public int RequiredDriverHours { get; init; }

    public int EnteredAvailabilityHours { get; init; }

    public int DriversWithoutAvailability { get; init; }
}

public sealed class RosterEmployeeResponse
{
    public Guid EmployeeId { get; init; }

    public string EmployeeName { get; init; } = string.Empty;

    public int TargetHours { get; init; }

    public int ScheduledHours { get; init; }

    public IReadOnlyList<RosterShiftResponse> Shifts { get; init; } = [];
}

public sealed class RosterShiftResponse
{
    public DateOnly Date { get; init; }

    public string StartTime { get; init; } = string.Empty;

    public string FinishTime { get; init; } = string.Empty;

    public int DurationHours { get; init; }
}

public sealed class RosterGenerationProgressResponse
{
    public Guid JobId { get; init; }

    public int WeekOffset { get; init; }

    public DateOnly WeekStart { get; init; }

    public string Status { get; init; } = string.Empty;

    public int Progress { get; init; }

    public string Message { get; init; } = string.Empty;

    public Guid? RosterPlanId { get; init; }

    public int? TotalScheduledHours { get; init; }
}
