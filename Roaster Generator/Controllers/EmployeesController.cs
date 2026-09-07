using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Roaster_Generator.Contracts.Employees;
using Roaster_Generator.Contracts.Schedules;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Security;
using Roaster_Generator.Services;

namespace Roaster_Generator.Controllers;

[ApiController]
[Authorize]
[Route("api/employees")]
public sealed class EmployeesController(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    IValidator<WeekSelectionRequest> weekValidator,
    IValidator<WeeklyScheduleRequest> scheduleValidator,
    WeeklyScheduleService schedules) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetEmployees(CancellationToken cancellationToken)
    {
        var query = db.Employees.AsNoTracking();

        if (!User.IsInRole(RoleNames.Admin))
        {
            var user = await userManager.GetUserAsync(User);

            if (user?.EmployeeId is not Guid employeeId)
            {
                return Unauthorized();
            }

            query = query.Where(employee => employee.Id == employeeId && employee.IsActive);
        }

        var employees = (await query
                .OrderBy(employee => employee.LastName)
                .ThenBy(employee => employee.FirstName)
                .ToListAsync(cancellationToken))
            .Select(ToEmployeeResponse)
            .ToList();

        return Ok(employees);
    }

    [HttpGet("{employeeId:guid}/schedule")]
    public async Task<IActionResult> GetSchedule(
        Guid employeeId,
        [FromQuery] int? weekOffset,
        CancellationToken cancellationToken)
    {
        var accessResult = await CheckScheduleAccessAsync(employeeId, cancellationToken);

        if (accessResult is not null)
        {
            return accessResult;
        }

        var selection = new WeekSelectionRequest
        {
            WeekOffset = weekOffset ?? WeeklyScheduleService.MinWeekOffset
        };
        var validationResult = await weekValidator.ValidateAsync(selection, cancellationToken);

        if (!validationResult.IsValid)
        {
            return ValidationError(ToErrors(validationResult), "The selected week is invalid.");
        }

        var schedule = await schedules.GetWeekAsync(
            employeeId,
            selection.WeekOffset,
            cancellationToken);

        return schedule is null
            ? NotFound(new { message = "Employee not found." })
            : Ok(schedule);
    }

    [HttpPut("{employeeId:guid}/schedule")]
    public async Task<IActionResult> SaveSchedule(
        Guid employeeId,
        [FromBody] WeeklyScheduleRequest request,
        CancellationToken cancellationToken)
    {
        var accessResult = await CheckScheduleAccessAsync(employeeId, cancellationToken);

        if (accessResult is not null)
        {
            return accessResult;
        }

        var validationResult = await scheduleValidator.ValidateAsync(request, cancellationToken);

        if (!validationResult.IsValid)
        {
            return ValidationError(ToErrors(validationResult), "The shift schedule is invalid.");
        }

        if (employeeId == Guid.Empty)
        {
            return ValidationError(
                new Dictionary<string, string[]>
                {
                    ["employeeId"] = ["Employee ID must not be empty."]
                },
                "The shift schedule is invalid.");
        }

        var schedule = await schedules.ReplaceWeekAsync(
            employeeId,
            request,
            cancellationToken);

        return schedule is null
            ? NotFound(new { message = "Employee not found." })
            : Ok(schedule);
    }

    private async Task<IActionResult?> CheckScheduleAccessAsync(
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        if (User.IsInRole(RoleNames.Admin))
        {
            return null;
        }

        var user = await userManager.GetUserAsync(User);

        return user?.EmployeeId != employeeId ||
            !await db.Employees.AnyAsync(
                employee => employee.Id == employeeId && employee.IsActive,
                cancellationToken)
            ? Forbid()
            : null;
    }

    private static Dictionary<string, string[]> ToErrors(FluentValidation.Results.ValidationResult result) =>
        result.Errors
            .GroupBy(error => error.PropertyName)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.ErrorMessage).ToArray());

    private static EmployeeResponse ToEmployeeResponse(Employee employee) => new()
    {
        Id = employee.Id,
        EmployeeNumber = employee.EmployeeNumber,
        FirstName = employee.FirstName,
        LastName = employee.LastName,
        PhoneNumber = employee.PhoneNumber,
        IsActive = employee.IsActive
    };
}
