using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Roaster_Generator.Contracts.Holidays;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Enums;
using Roaster_Generator.Security;
using Roaster_Generator.Services;

namespace Roaster_Generator.Controllers;

[ApiController]
[Authorize]
[Route("api/holidays")]
public sealed class HolidaysController(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    IValidator<HolidayHoursRequest> holidayValidator) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var employee = await GetCurrentEmployeeAsync(cancellationToken);
        if (employee is null)
        {
            return Forbid();
        }

        var requests = await db.HolidayRequests
            .AsNoTracking()
            .Include(request => request.Employee)
            .Where(request => request.EmployeeId == employee.Id)
            .OrderByDescending(request => request.UpdatedAtUtc)
            .ToListAsync(cancellationToken);

        return Ok(new EmployeeHolidayResponse
        {
            Requested = requests
                .Where(request => request.Status == HolidayStatus.Requested)
                .Select(HolidayResponseMapper.ToResponse)
                .FirstOrDefault(),
            Used = requests
                .Where(request => request.Status == HolidayStatus.Used)
                .Select(HolidayResponseMapper.ToResponse)
                .ToList()
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] HolidayHoursRequest request,
        CancellationToken cancellationToken)
    {
        var employee = await GetCurrentEmployeeAsync(cancellationToken);
        if (employee is null)
        {
            return Forbid();
        }

        var validationResult = await holidayValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return ValidationError(ToErrors(validationResult), "The holiday request is invalid.");
        }

        if (await db.HolidayRequests.AnyAsync(
                item => item.EmployeeId == employee.Id && item.Status == HolidayStatus.Requested,
                cancellationToken))
        {
            return Conflict(new { message = "You already have an active holiday request. Edit or remove it first." });
        }

        var now = DateTimeOffset.UtcNow;
        var holiday = new HolidayRequest
        {
            EmployeeId = employee.Id,
            Hours = request.Hours,
            Status = HolidayStatus.Requested,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.HolidayRequests.Add(holiday);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Conflict(new { message = "You already have an active holiday request. Edit or remove it first." });
        }

        holiday.Employee = employee;
        return Ok(HolidayResponseMapper.ToResponse(holiday));
    }

    [HttpPut("{holidayId:guid}")]
    public async Task<IActionResult> Update(
        Guid holidayId,
        [FromBody] HolidayHoursRequest request,
        CancellationToken cancellationToken)
    {
        var employee = await GetCurrentEmployeeAsync(cancellationToken);
        if (employee is null)
        {
            return Forbid();
        }

        var validationResult = await holidayValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return ValidationError(ToErrors(validationResult), "The holiday request is invalid.");
        }

        var holiday = await db.HolidayRequests
            .Include(item => item.Employee)
            .SingleOrDefaultAsync(
                item => item.Id == holidayId &&
                        item.EmployeeId == employee.Id &&
                        item.Status == HolidayStatus.Requested,
                cancellationToken);
        if (holiday is null)
        {
            return NotFound(new { message = "Active holiday request not found." });
        }

        holiday.Hours = request.Hours;
        holiday.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return Ok(HolidayResponseMapper.ToResponse(holiday));
    }

    [HttpDelete("{holidayId:guid}")]
    public async Task<IActionResult> Delete(
        Guid holidayId,
        CancellationToken cancellationToken)
    {
        var employee = await GetCurrentEmployeeAsync(cancellationToken);
        if (employee is null)
        {
            return Forbid();
        }

        var holiday = await db.HolidayRequests.SingleOrDefaultAsync(
            item => item.Id == holidayId &&
                    item.EmployeeId == employee.Id &&
                    item.Status == HolidayStatus.Requested,
            cancellationToken);
        if (holiday is null)
        {
            return NotFound(new { message = "Active holiday request not found." });
        }

        db.HolidayRequests.Remove(holiday);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<Employee?> GetCurrentEmployeeAsync(CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user?.EmployeeId is not Guid employeeId)
        {
            return null;
        }

        var roles = await userManager.GetRolesAsync(user);
        if (!roles.Any(RoleNames.IsEmployeeRole))
        {
            return null;
        }

        return await db.Employees.SingleOrDefaultAsync(
            employee => employee.Id == employeeId && employee.IsActive,
            cancellationToken);
    }
}
