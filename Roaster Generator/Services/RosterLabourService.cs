using Microsoft.EntityFrameworkCore;
using Roaster_Generator.Contracts.Roster;
using Roaster_Generator.Data;
using Roaster_Generator.Security;

namespace Roaster_Generator.Services;

public sealed class RosterLabourService(AppDbContext db)
{
    public async Task<RosterLabourResponse> GetAsync(DateOnly weekStart, CancellationToken ct)
    {
        var plans = await db.RosterPlans.AsNoTracking()
            .Where(plan => plan.WeekStart == weekStart && plan.RosterKind == RosterKinds.Drivers)
            .Select(plan => plan.Id).ToArrayAsync(ct);
        var hasDrivers = plans.Length > 0;
        var shifts = await db.RosterShifts.AsNoTracking()
            .Where(shift => plans.Contains(shift.RosterPlanId))
            .Select(shift => new LabourShift(
                shift.EmployeeId, shift.Date, shift.StartTime,
                shift.FinishTime, shift.Employee.HourlyRate, shift.Employee.User != null,
                shift.Employee.ManagerProfile != null))
            .ToListAsync(ct);
        var employeeIds = shifts.Select(shift => shift.EmployeeId).Distinct().ToArray();
        var managerIds = (await db.UserRoles
            .Join(db.Roles.Where(role => role.Name == RoleNames.Manager),
                membership => membership.RoleId, role => role.Id, (membership, role) => membership.UserId)
            .Join(db.Users.Where(user => user.EmployeeId.HasValue && employeeIds.Contains(user.EmployeeId.Value)),
                userId => userId, user => user.Id, (userId, user) => user.EmployeeId!.Value)
            .ToListAsync(ct)).ToHashSet();

        // Match generation: demand is the latest reusable weekday template, not a
        // plan whose original import date happens to equal the saved roster week.
        var demand = await db.DemandPlans.AsNoTracking().Include(plan => plan.Columns)
            .OrderByDescending(plan => plan.UpdatedAtUtc).ThenByDescending(plan => plan.WeekStart)
            .FirstOrDefaultAsync(ct);
        var sales = Enumerable.Range(0, 7).Select(position =>
        {
            var column = demand?.Columns.SingleOrDefault(column => column.Position == position);
            return column is { TargetSales: >= 0 } ? column.TargetSales : (decimal?)null;
        }).ToArray();
        decimal? weeklySales = sales.All(value => value.HasValue) ? sales.Sum(value => value!.Value) : null;
        var driverDays = new Amounts[7];
        foreach (var shift in shifts)
        {
            var position = shift.Date.DayNumber - weekStart.DayNumber;
            if (position is < 0 or > 6)
                throw new InvalidOperationException("A saved roster shift is outside its roster week; correct the shift before calculating labour.");
            var isManager = managerIds.Contains(shift.EmployeeId) || !shift.HasUser && shift.HasManagerProfile;
            var amount = CalculateShift(shift, isManager);
            driverDays[position] += amount;
        }

        // Round daily costs to cents before summing the weekly total.
        for (var position = 0; position < 7; position++)
        {
            driverDays[position] = RoundCost(driverDays[position]);
        }

        var daysResponse = Enumerable.Range(0, 7).Select(position => new RosterLabourDayResponse
        {
            Date = weekStart.AddDays(position), Label = weekStart.AddDays(position).DayOfWeek.ToString(),
            TargetSales = sales[position],
            Drivers = Totals(driverDays[position], sales[position], hasDrivers)
        }).ToArray();
        var drivers = driverDays.Aggregate(default(Amounts), (sum, day) => sum + day);
        var warnings = new List<string>();
        if (!hasDrivers) warnings.Add("No saved driver roster exists for this week. Generate a driver roster to calculate labour.");
        if (demand is null) warnings.Add("No demand template is available. Enter target sales to calculate labour percentages.");
        else if (sales.Any(value => value is null or <= 0))
            warnings.Add("Some days have missing or zero target sales. Labour percentages are unavailable for those days.");
        return new RosterLabourResponse
        {
            WeekStart = weekStart, DemandPlanId = demand?.Id,
            HasDriverRoster = hasDrivers, TargetSales = weeklySales,
            Drivers = Totals(drivers, weeklySales, hasDrivers),
            Days = daysResponse, Warnings = warnings
        };
    }

    private static Amounts CalculateShift(LabourShift shift, bool isManager)
    {
        // Saved times use the same business-day convention as roster responses:
        // starts before 06:00 belong to the following calendar day.
        var start = shift.Date.ToDateTime(shift.StartTime);
        if (shift.StartTime.Hour < 6) start = start.AddDays(1);
        var finish = shift.Date.ToDateTime(shift.FinishTime);
        while (finish <= start) finish = finish.AddDays(1);

        var result = default(Amounts);
        for (var cursor = start; cursor < finish;)
        {
            var midnight = cursor.Date.AddDays(1);
            var end = finish < midnight ? finish : midnight;
            var hours = (decimal)(end - cursor).Ticks / TimeSpan.TicksPerHour;
            var multiplier = !isManager && cursor.DayOfWeek == DayOfWeek.Sunday ? 1.25m : 1m;
            result += new Amounts(hours, hours * shift.HourlyRate * multiplier);
            cursor = end;
        }
        return result;
    }

    private static RosterLabourTotalsResponse Totals(Amounts amounts, decimal? sales, bool complete) => new()
    {
        IsComplete = complete,
        ScheduledHours = decimal.Round(amounts.Hours, 4, MidpointRounding.AwayFromZero),
        LabourCost = amounts.Cost,
        LabourPercentage = complete && sales > 0
            ? decimal.Round(100m * amounts.Cost / sales.Value, 2, MidpointRounding.AwayFromZero) : null
    };

    private static Amounts RoundCost(Amounts amounts) =>
        amounts with { Cost = decimal.Round(amounts.Cost, 2, MidpointRounding.AwayFromZero) };

    private sealed record LabourShift(Guid EmployeeId, DateOnly Date,
        TimeOnly StartTime, TimeOnly FinishTime, decimal HourlyRate, bool HasUser, bool HasManagerProfile);

    private readonly record struct Amounts(decimal Hours, decimal Cost)
    {
        public static Amounts operator +(Amounts left, Amounts right) =>
            new(left.Hours + right.Hours, left.Cost + right.Cost);
    }
}
