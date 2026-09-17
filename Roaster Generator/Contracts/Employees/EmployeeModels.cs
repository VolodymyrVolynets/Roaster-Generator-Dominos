using Roaster_Generator.Enums;

namespace Roaster_Generator.Contracts.Employees;

public sealed class EmployeeRequest
{
    public string EmployeeNumber { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    public string? PayrollNumber { get; set; }

    // Older clients may omit pay; updates preserve the existing value in that case.
    public decimal? HourlyRate { get; set; }

    // Omitted values preserve the current limit on updates; new employees default to 45.
    public int? MaximumWeeklyHours { get; set; }

    public List<string> Roles { get; set; } = ["Driver"];

    public DriverType DriverType { get; set; } = DriverType.Car;

    // Preserve ownership when older clients omit it. New driver profiles default to true.
    public bool? IsOwn { get; set; }
}

public sealed class EmployeeResponse
{
    public Guid Id { get; init; }

    public string EmployeeNumber { get; init; } = string.Empty;

    public string FirstName { get; init; } = string.Empty;

    public string LastName { get; init; } = string.Empty;

    public string PhoneNumber { get; init; } = string.Empty;

    public string? PayrollNumber { get; init; }

    public decimal HourlyRate { get; init; }

    public int MaximumWeeklyHours { get; init; }

    public bool IsActive { get; init; }

    public IReadOnlyList<string> Roles { get; init; } = [];

    public DriverType? DriverType { get; init; }

    public bool? IsOwn { get; init; }
}
