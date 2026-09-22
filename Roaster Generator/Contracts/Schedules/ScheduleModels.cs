namespace Roaster_Generator.Contracts.Schedules;

public sealed class WeeklyScheduleRequest
{
    public int WeekOffset { get; set; }

    public List<ScheduleDayRequest> Days { get; set; } = [];
}

public sealed class WeekSelectionRequest
{
    public int WeekOffset { get; init; }
}

public sealed class ScheduleDayRequest
{
    public DateOnly Date { get; set; }

    public TimeOnly? StartTime { get; set; }

    public TimeOnly? FinishTime { get; set; }
}

public sealed class WeeklyScheduleResponse
{
    public Guid EmployeeId { get; init; }

    public string EmployeeName { get; init; } = string.Empty;

    public DateOnly WeekStart { get; init; }

    public DateOnly WeekEnd { get; init; }

    // Null means the administrator has no historical lower bound.
    public int? MinimumEditableWeekOffset { get; set; } = 1;

    public bool CanEdit { get; set; } = true;

    // Entry limits for the authenticated caller; null allows administrator overrides
    // or indicates an employee role that is not subject to driver entry limits.
    public int? MaximumAvailabilityHoursPerDay { get; set; }

    public int? MaximumAvailabilityDaysPerWeek { get; set; }

    public double? ApproximateHours { get; init; }

    public double? ApproximateCapacityHours { get; init; }

    // Aggregate only; present for eligible drivers, including when demand is missing.
    public int? DriversWithoutAvailability { get; init; }

    public AvailabilityHeatmapResponse? Heatmap { get; init; }

    public IReadOnlyList<ScheduleDayResponse> Days { get; init; } = [];
}

public sealed class WeeklyAvailabilityResponse
{
    public DateOnly WeekStart { get; init; }

    public DateOnly WeekEnd { get; init; }

    public AvailabilityHeatmapResponse Heatmap { get; init; } = new();

    public IReadOnlyList<WeeklyScheduleResponse> Employees { get; init; } = [];
}

public sealed class AvailabilityHeatmapResponse
{
    public bool DemandPlanExists { get; init; }

    public string Message { get; init; } = string.Empty;

    public IReadOnlyList<AvailabilityHeatmapSlotResponse> Slots { get; init; } = [];
}

public sealed class AvailabilityHeatmapSlotResponse
{
    public DateOnly Date { get; init; }

    public string DayOfWeek { get; init; } = string.Empty;

    public int Hour { get; init; }

    public string StartTime { get; init; } = string.Empty;

    public int StartDayOffset { get; init; }

    public int RequiredDrivers { get; init; }

    public int AvailableDrivers { get; init; }

    public int ShortageDrivers { get; init; }

    public double ScarcityScore { get; init; }

    public string Level { get; init; } = string.Empty;
}

public sealed class ScheduleDayResponse
{
    public DateOnly Date { get; init; }

    public string DayOfWeek { get; init; } = string.Empty;

    public string? StartTime { get; init; }

    public string? FinishTime { get; init; }
}
