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

    public IReadOnlyList<ScheduleDayResponse> Days { get; init; } = [];
}

public sealed class WeeklyAvailabilityResponse
{
    public DateOnly WeekStart { get; init; }

    public DateOnly WeekEnd { get; init; }

    public IReadOnlyList<WeeklyScheduleResponse> Employees { get; init; } = [];
}

public sealed class ScheduleDayResponse
{
    public DateOnly Date { get; init; }

    public string DayOfWeek { get; init; } = string.Empty;

    public string? StartTime { get; init; }

    public string? FinishTime { get; init; }
}
