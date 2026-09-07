namespace Roaster_Generator.Entities;

public sealed class Employee
{
    public Guid Id { get; set; }

    public string EmployeeNumber { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    public ICollection<Shift> Shifts { get; set; } = new List<Shift>();
}
