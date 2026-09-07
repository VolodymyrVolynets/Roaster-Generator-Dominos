using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Roaster_Generator.Contracts.Roster;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;

namespace Roaster_Generator.Services;

public sealed class RosterPlanService(AppDbContext db)
{
    public async Task<RosterPlanResponse?> GetAsync(
        int weekOffset,
        CancellationToken cancellationToken)
    {
        var weekStart = WeeklyScheduleService.GetWeekMonday(weekOffset);
        var plan = await db.RosterPlans
            .AsNoTracking()
            .Include(item => item.Shifts)
            .ThenInclude(shift => shift.Employee)
            .SingleOrDefaultAsync(item => item.WeekStart == weekStart, cancellationToken);

        return plan is null ? null : ToResponse(plan);
    }

    private static RosterPlanResponse ToResponse(RosterPlan plan)
    {
        var employees = plan.Shifts
            .GroupBy(shift => shift.Employee)
            .OrderBy(group => group.Key.LastName)
            .ThenBy(group => group.Key.FirstName)
            .Select(group => new RosterEmployeeResponse
            {
                EmployeeId = group.Key.Id,
                EmployeeName = $"{group.Key.FirstName} {group.Key.LastName}".Trim(),
                TargetHours = group.Key.TargetHours,
                ScheduledHours = group.Sum(GetDurationHours),
                Shifts = group
                    .OrderBy(shift => shift.Date)
                    .ThenBy(shift => shift.StartTime)
                    .Select(shift => new RosterShiftResponse
                    {
                        Date = shift.Date,
                        StartTime = shift.StartTime.ToString("HH", CultureInfo.InvariantCulture),
                        FinishTime = shift.FinishTime.ToString("HH", CultureInfo.InvariantCulture),
                        DurationHours = GetDurationHours(shift)
                    })
                    .ToList()
            })
            .ToList();

        return new RosterPlanResponse
        {
            Id = plan.Id,
            WeekStart = plan.WeekStart,
            UpdatedAtUtc = plan.UpdatedAtUtc,
            TotalDemandHours = employees.Sum(employee => employee.ScheduledHours),
            TotalScheduledHours = employees.Sum(employee => employee.ScheduledHours),
            Employees = employees
        };
    }

    private static int GetDurationHours(RosterShift shift)
    {
        var start = shift.StartTime.Hour;
        var finish = shift.FinishTime.Hour;
        return finish > start ? finish - start : 24 - start + finish;
    }
}
