namespace Roaster_Generator.Contracts.Employees;

public sealed class EmployeeRequest
{
    public string EmployeeNumber { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    public int TargetHours { get; set; } = 20;

    public bool CanWorkAlone { get; set; } = true;
}

public sealed class EmployeeResponse
{
    public Guid Id { get; init; }

    public string EmployeeNumber { get; init; } = string.Empty;

    public string FirstName { get; init; } = string.Empty;

    public string LastName { get; init; } = string.Empty;

    public string PhoneNumber { get; init; } = string.Empty;

    public bool IsActive { get; init; }

    public int TargetHours { get; init; }

    public bool CanWorkAlone { get; init; }
}
