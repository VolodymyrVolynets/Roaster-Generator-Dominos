namespace Roaster_Generator.Entities;

public sealed class InStoreProfile
{
    public Guid EmployeeId { get; set; }

    public Employee Employee { get; set; } = null!;
}
