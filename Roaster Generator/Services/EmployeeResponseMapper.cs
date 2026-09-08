using Roaster_Generator.Contracts.Employees;
using Roaster_Generator.Entities;

namespace Roaster_Generator.Services;

public static class EmployeeResponseMapper
{
    public static EmployeeResponse ToResponse(
        Employee employee,
        IEnumerable<string> roles) => new()
    {
        Id = employee.Id,
        EmployeeNumber = employee.EmployeeNumber,
        FirstName = employee.FirstName,
        LastName = employee.LastName,
        PhoneNumber = employee.PhoneNumber,
        PayrollNumber = employee.PayrollNumber,
        IsActive = employee.IsActive,
        Roles = roles.ToArray(),
        TargetHours = employee.DriverProfile?.TargetHours,
        InsideTargetHours = employee.ManagerProfile?.TargetHours ?? employee.InStoreProfile?.TargetHours,
        DriverType = employee.DriverProfile?.DriverType
    };
}
