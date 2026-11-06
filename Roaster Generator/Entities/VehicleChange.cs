using Roaster_Generator.Enums;

namespace Roaster_Generator.Entities;

public sealed class VehicleChange
{
    public Guid Id { get; set; }

    public Guid EmployeeId { get; set; }

    public Employee Employee { get; set; } = null!;

    public string OldRegistration { get; set; } = string.Empty;

    public string NewRegistration { get; set; } = string.Empty;

    public VehicleChangeStatus Status { get; set; } = VehicleChangeStatus.Open;

    public DateTimeOffset StartedAtUtc { get; set; }

    public Guid? StartedByUserId { get; set; }

    public string StartedByName { get; set; } = string.Empty;

    public DateTimeOffset? ClosedAtUtc { get; set; }
}
