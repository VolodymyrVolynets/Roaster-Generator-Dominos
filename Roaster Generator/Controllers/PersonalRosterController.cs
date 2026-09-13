using System.Globalization;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Roaster_Generator.Contracts.Roster;
using Roaster_Generator.Contracts.Schedules;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Security;
using Roaster_Generator.Services;

namespace Roaster_Generator.Controllers;

[ApiController]
[Authorize(Roles = RoleNames.Driver)]
[Route("api/roster/me")]
public sealed class PersonalRosterController(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    IValidator<WeekSelectionRequest> weekValidator) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] int? weekOffset,
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user?.EmployeeId is not Guid employeeId ||
            !await db.Employees.AnyAsync(
                employee => employee.Id == employeeId && employee.IsActive,
                cancellationToken))
        {
            return Forbid();
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

        var weekStart = WeeklyScheduleService.GetWeekMonday(selection.WeekOffset);
        var plan = await db.RosterPlans
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.WeekStart == weekStart && item.RosterKind == RosterKinds.Drivers,
                cancellationToken);

        if (plan is null)
        {
            return Ok(new PersonalRosterResponse { WeekStart = weekStart });
        }

        var shiftEntities = await db.RosterShifts
            .AsNoTracking()
            .Where(shift => shift.RosterPlanId == plan.Id && shift.EmployeeId == employeeId)
            .OrderBy(shift => shift.Date)
            .ThenBy(shift => shift.StartTime)
            .ToListAsync(cancellationToken);
        var shifts = shiftEntities
            .Select(shift => new RosterShiftResponse
            {
                Date = shift.Date,
                StartTime = shift.StartTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                FinishTime = shift.FinishTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                DurationHours = (shift.FinishTime.Hour - shift.StartTime.Hour + 24) % 24,
                StartDayOffset = shift.StartTime.Hour < 6 ? 1 : 0,
                FinishDayOffset = shift.StartTime.Hour < 6 || shift.FinishTime <= shift.StartTime ? 1 : 0
            })
            .ToList();

        return Ok(new PersonalRosterResponse
        {
            WeekStart = weekStart,
            HasPublishedRoster = true,
            PublishedAtUtc = plan.UpdatedAtUtc,
            ScheduledHours = shifts.Sum(shift => shift.DurationHours),
            Shifts = shifts
        });
    }
}
