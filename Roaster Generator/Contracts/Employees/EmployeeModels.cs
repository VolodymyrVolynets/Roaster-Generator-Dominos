using Roaster_Generator.Enums;

namespace Roaster_Generator.Contracts.Employees;

public sealed class EmployeeRequest
{
    public string EmployeeNumber { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    public string? PayrollNumber { get; set; }

    public List<string> Roles { get; set; } = ["Driver"];

    public int TargetHours { get; set; } = 20;

    public int InsideTargetHours { get; set; } = 20;

    public DriverType DriverType { get; set; } = DriverType.Car;
}

public sealed class EmployeeResponse
{
    public Guid Id { get; init; }

    public string EmployeeNumber { get; init; } = string.Empty;

    public string FirstName { get; init; } = string.Empty;

    public string LastName { get; init; } = string.Empty;

    public string PhoneNumber { get; init; } = string.Empty;

    public string? PayrollNumber { get; init; }

    public bool IsActive { get; init; }

    public IReadOnlyList<string> Roles { get; init; } = [];

    public int? TargetHours { get; init; }

    public int? InsideTargetHours { get; init; }

    public DriverType? DriverType { get; init; }
}
