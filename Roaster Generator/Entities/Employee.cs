namespace Roaster_Generator.Entities;

public sealed class Employee
{
    public Guid Id { get; set; }

    public string EmployeeNumber { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    public string? PayrollNumber { get; set; }

    public decimal HourlyRate { get; set; } = 14.5m;

    public bool IsActive { get; set; } = true;

    public DriverProfile? DriverProfile { get; set; }

    public InStoreProfile? InStoreProfile { get; set; }

    public ManagerProfile? ManagerProfile { get; set; }

    public ICollection<Shift> Shifts { get; set; } = new List<Shift>();

    public ICollection<RosterShift> RosterShifts { get; set; } = new List<RosterShift>();

    public ApplicationUser? User { get; set; }
}
