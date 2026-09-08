using Roaster_Generator.Contracts.Holidays;
using Roaster_Generator.Entities;

namespace Roaster_Generator.Services;

public static class HolidayResponseMapper
{
    public static HolidayResponse ToResponse(HolidayRequest request) => new()
    {
        Id = request.Id,
        EmployeeId = request.EmployeeId,
        EmployeeName = $"{request.Employee.FirstName} {request.Employee.LastName}".Trim(),
        EmployeeNumber = request.Employee.EmployeeNumber,
        PayrollNumber = request.Employee.PayrollNumber,
        Hours = request.Hours,
        Status = request.Status,
        CreatedAtUtc = request.CreatedAtUtc,
        UpdatedAtUtc = request.UpdatedAtUtc,
        UsedAtUtc = request.UsedAtUtc
    };
}
