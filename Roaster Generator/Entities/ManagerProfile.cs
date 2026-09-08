namespace Roaster_Generator.Entities;

public sealed class ManagerProfile
{
    public Guid EmployeeId { get; set; }

    public int TargetHours { get; set; } = 20;

    public Employee Employee { get; set; } = null!;
}
