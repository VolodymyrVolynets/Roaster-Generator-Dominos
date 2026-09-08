namespace Roaster_Generator.Entities;

public sealed class ManagerProfile
{
    public Guid EmployeeId { get; set; }

    public Employee Employee { get; set; } = null!;
}
