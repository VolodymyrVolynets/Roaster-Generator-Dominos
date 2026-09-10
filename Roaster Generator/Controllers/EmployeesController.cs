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
    WeeklyScheduleService schedules,
    AvailabilityEditPolicy availabilityEdits) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetEmployees(CancellationToken cancellationToken)
    {
        var query = db.Employees.AsNoTracking();

        if (!User.IsInRole(RoleNames.Admin) && !User.IsInRole(RoleNames.Manager))
        {
            var user = await userManager.GetUserAsync(User);

            if (user?.EmployeeId is not Guid employeeId)
            {
                return Unauthorized();
            }

            query = query.Where(employee => employee.Id == employeeId && employee.IsActive);
        }

        var employeeEntities = await query
                .Include(employee => employee.DriverProfile)
                .Include(employee => employee.InStoreProfile)
                .Include(employee => employee.ManagerProfile)
                .OrderBy(employee => employee.LastName)
                .ThenBy(employee => employee.FirstName)
                .ToListAsync(cancellationToken);

        var employees = new List<EmployeeResponse>(employeeEntities.Count);

        foreach (var employee in employeeEntities)
        {
            var user = await userManager.Users
                .SingleOrDefaultAsync(item => item.EmployeeId == employee.Id, cancellationToken);
            var roles = user is null
                ? []
                : await userManager.GetRolesAsync(user);
            employees.Add(EmployeeResponseMapper.ToResponse(employee, roles));
        }

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

        if (schedule is null) return NotFound(new { message = "Employee not found." });

        ApplyEditAccess(schedule, selection.WeekOffset);
        return Ok(schedule);
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

        var minimumEditableWeekOffset = availabilityEdits.GetMinimumEditableWeekOffset(
            User.IsInRole(RoleNames.Admin));
        if (request.WeekOffset < minimumEditableWeekOffset)
        {
            return ValidationError(
                new Dictionary<string, string[]>
                {
                    [nameof(request.WeekOffset)] = [AvailabilityEditPolicy.WeekendLockMessage]
                },
                "This availability week is locked.");
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

        if (schedule is null) return NotFound(new { message = "Employee not found." });

        ApplyEditAccess(schedule, request.WeekOffset);
        return Ok(schedule);
    }

    private void ApplyEditAccess(WeeklyScheduleResponse schedule, int weekOffset)
    {
        schedule.MinimumEditableWeekOffset = availabilityEdits.GetMinimumEditableWeekOffset(
            User.IsInRole(RoleNames.Admin));
        schedule.CanEdit = weekOffset >= schedule.MinimumEditableWeekOffset;
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

}
