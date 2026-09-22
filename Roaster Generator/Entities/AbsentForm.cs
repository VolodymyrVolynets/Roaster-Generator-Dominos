namespace Roaster_Generator.Entities;

public sealed class AbsentForm
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;

    // Preserve the submitted details even if the employee or saved roster changes.
    public string DriverFullName { get; set; } = string.Empty;
    public string? PayrollNumber { get; set; }
    public Guid? SavedRosterShiftId { get; set; }
    public DateOnly ShiftDate { get; set; }
    public TimeOnly ShiftStartTime { get; set; }
    public TimeOnly ShiftFinishTime { get; set; }
    public DateOnly NotificationDate { get; set; }
    public TimeOnly NotificationTime { get; set; }
    public string NotificationMethod { get; set; } = string.Empty;
    public string CancellationReason { get; set; } = string.Empty;
    public DateTimeOffset SubmittedAtUtc { get; set; }
}
