namespace Roaster_Generator.Entities;

/// <summary>
/// The vehicle an employee currently drives. Kept apart from <see cref="DriverProfile"/>
/// because drivers need to see it and profile details stay out of the driver workspace.
/// </summary>
public sealed class EmployeeVehicle
{
    public Guid EmployeeId { get; set; }

    public Employee Employee { get; set; } = null!;

    public string Registration { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
