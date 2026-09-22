namespace Roaster_Generator.Contracts.Absent;

public sealed class AbsentFormCreateRequest
{
    public Guid? SavedRosterShiftId { get; set; }
    public DateOnly? ShiftDate { get; set; }
    public string? ShiftStartTime { get; set; }
    public string? ShiftFinishTime { get; set; }
    public DateOnly NotificationDate { get; set; }
    public string NotificationTime { get; set; } = string.Empty;
    public string NotificationMethod { get; set; } = string.Empty;
    public string CancellationReason { get; set; } = string.Empty;
}

public sealed class AbsentFormUpdateRequest
{
    // Null keeps the recorded shift snapshot. A supplied shift must still belong
    // to this driver and a saved driver roster. Supplying all three manual shift
    // fields replaces the snapshot with a manually entered shift.
    public Guid? SavedRosterShiftId { get; set; }
    public DateOnly? ShiftDate { get; set; }
    public string? ShiftStartTime { get; set; }
    public string? ShiftFinishTime { get; set; }
    public DateOnly NotificationDate { get; set; }
    public string NotificationTime { get; set; } = string.Empty;
    public string NotificationMethod { get; set; } = string.Empty;
    public string CancellationReason { get; set; } = string.Empty;
}

public sealed class AbsentShiftResponse
{
    public Guid? Id { get; init; }
    public DateOnly Date { get; init; }
    public string StartTime { get; init; } = string.Empty;
    public string FinishTime { get; init; } = string.Empty;
    public int StartDayOffset { get; init; }
    public int FinishDayOffset { get; init; }
}

public sealed class AbsentFormResponse
{
    public Guid Id { get; init; }
    public Guid EmployeeId { get; init; }
    public string DriverFullName { get; init; } = string.Empty;
    public string? PayrollNumber { get; init; }
    public AbsentShiftResponse Shift { get; init; } = new();
    public DateOnly NotificationDate { get; init; }
    public string NotificationTime { get; init; } = string.Empty;
    public string NotificationMethod { get; init; } = string.Empty;
    public string CancellationReason { get; init; } = string.Empty;
    public DateTimeOffset SubmittedAtUtc { get; init; }
}

public sealed class DriverAbsentOverviewResponse
{
    public string DriverFullName { get; init; } = string.Empty;
    public string? PayrollNumber { get; init; }
    public DateOnly NotificationDate { get; init; }
    public IReadOnlyList<AbsentShiftResponse> Shifts { get; init; } = [];
    public IReadOnlyList<AbsentFormResponse> Forms { get; init; } = [];
}

public sealed class DriverAbsentGroupResponse
{
    public Guid EmployeeId { get; init; }
    public string DriverFullName { get; init; } = string.Empty;
    public IReadOnlyList<AbsentShiftResponse> Shifts { get; init; } = [];
    public IReadOnlyList<AbsentFormResponse> Forms { get; init; } = [];
}
