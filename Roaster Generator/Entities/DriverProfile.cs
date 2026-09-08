using Roaster_Generator.Enums;

namespace Roaster_Generator.Entities;

public sealed class DriverProfile
{
    public Guid EmployeeId { get; set; }

    public int TargetHours { get; set; } = 20;

    public DriverType DriverType { get; set; } = DriverType.Car;

    public Employee Employee { get; set; } = null!;
}
