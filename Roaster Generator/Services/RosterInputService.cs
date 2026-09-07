using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Roaster_Generator.Configuration;
using Roaster_Generator.Contracts.Roster;
using Roaster_Generator.Data;

namespace Roaster_Generator.Services;

public sealed class RosterInputException(string message, IReadOnlyList<string>? diagnostics = null) : Exception(message)
{
    public IReadOnlyList<string> Diagnostics { get; } = diagnostics ?? [message];
}

public sealed record LoadedRosterInput(RosterSolverInput Input, RosterSettingsRequest Settings,
    string Fingerprint, IReadOnlyList<string> Warnings);

public sealed class RosterInputService(AppDbContext db, RosterSettingsService settingsService,
    IOptions<ShopHoursOptions> shopHoursOptions)
{
    public async Task<LoadedRosterInput> LoadAsync(DateOnly weekStart, CancellationToken ct)
    {
        var weekEnd = weekStart.AddDays(7);
        // Demand is deliberately a single reusable Monday-to-Sunday template in this app.
        var plan = await db.DemandPlans.AsNoTracking().Include(p => p.Columns)
            .Include(p => p.Rows).ThenInclude(r => r.Values)
            .OrderByDescending(p => p.UpdatedAtUtc).ThenByDescending(p => p.WeekStart).FirstOrDefaultAsync(ct)
            ?? throw new RosterInputException("Import a weekly demand template before generating a roster.");
        if (plan.Columns.Count != 7 || !plan.Columns.Select(c => c.Position).Order().SequenceEqual(Enumerable.Range(0, 7)))
            throw new RosterInputException("The demand template must contain exactly Monday through Sunday.");
        var diagnostics = new List<string>();
        var warnings = new List<string>();
        var demand = new List<RosterSolverDemand>();
        foreach (var column in plan.Columns.OrderBy(c => c.Position))
        {
            var date = weekStart.AddDays(column.Position);
            var hours = shopHoursOptions.Value.For(date.DayOfWeek);
            if (hours.OpeningTime.Minute != 0 || hours.ClosingTime.Minute != 0)
                throw new RosterInputException("Shop opening and closing times must be whole hours for hourly demand.");
            var opening = hours.OpeningTime.Hour;
            var closing = hours.ClosingTime.Hour;
            if (closing <= opening) closing += 24;
            var values = plan.Rows.ToDictionary(r => r.Hour,
                r => r.Values.SingleOrDefault(v => v.DemandColumnId == column.Id)?.Demand);
            for (var hour = opening; hour < closing; hour++)
            {
                if (!values.TryGetValue(hour % 24, out var required) || required is null || required < 0)
                    diagnostics.Add($"{date.DayOfWeek} {hour % 24:00}:00{(hour >= 24 ? " (+1 day)" : "")}: enter a non-negative driver demand; this hour is missing or invalid.");
                else demand.Add(new RosterSolverDemand(date, hour, required.Value));
            }
            if (values.Any(v => v.Value > 0 && !Enumerable.Range(opening, closing - opening).Any(h => h % 24 == v.Key)))
                warnings.Add($"{date.DayOfWeek}: demand entries outside configured shop hours are excluded.");
        }
        if (diagnostics.Count > 0)
            throw new RosterInputException("The demand template is incomplete. Enter demand for every open hour (use 0 when no drivers are needed).", diagnostics);

        var employees = await db.Employees.AsNoTracking().Where(e => e.IsActive).OrderBy(e => e.Id).ToListAsync(ct);
        var ids = employees.Select(e => e.Id).ToArray();
        var availability = await db.Shifts.AsNoTracking().Where(s => ids.Contains(s.EmployeeId) && s.Date >= weekStart && s.Date < weekEnd)
            .OrderBy(s => s.EmployeeId).ThenBy(s => s.Date).ThenBy(s => s.StartTime).ToListAsync(ct);
        var boundaryEntities = await db.RosterShifts.AsNoTracking()
            .Where(s => ids.Contains(s.EmployeeId) && s.Date >= weekStart.AddDays(-3) && s.Date < weekEnd.AddDays(3)
                && (s.Date < weekStart || s.Date >= weekEnd))
            .OrderBy(s => s.EmployeeId).ThenBy(s => s.Date).ThenBy(s => s.StartTime).ToListAsync(ct);
        var boundaries = boundaryEntities.Select(s =>
        {
            var startHour = s.StartTime.Hour;
            if (startHour < 6) startHour += 24;
            var finishHour = s.FinishTime.Hour;
            while (finishHour <= startHour) finishHour += 24;
            return new RosterSolverBoundaryShift(s.EmployeeId, s.Date.ToDateTime(TimeOnly.MinValue).AddHours(startHour),
                s.Date.ToDateTime(TimeOnly.MinValue).AddHours(finishHour));
        }).ToList();
        var settings = await settingsService.GetAsync(ct);
        foreach (var employee in employees)
        {
            if (employee.TargetHours == 0)
                warnings.Add($"{employee.FirstName} {employee.LastName}: target hours are 0; available as a reserve, excluded from percentage balancing (percentage is undefined).");
            else if (!availability.Any(s => s.EmployeeId == employee.Id))
                warnings.Add($"{employee.FirstName} {employee.LastName}: no availability entered; target percentage will be 0%.");
        }
        var historyPlans = await db.RosterPlans.AsNoTracking()
            .Where(p => p.WeekStart >= weekStart.AddDays(-28) && p.WeekStart < weekStart)
            .OrderBy(p => p.WeekStart).Select(p => new { p.WeekStart, p.SnapshotJson }).ToListAsync(ct);
        var history = new List<RosterSolverHistory>();
        foreach (var historicalPlan in historyPlans)
        {
            if (historicalPlan.SnapshotJson is null)
            {
                warnings.Add($"Week {historicalPlan.WeekStart:yyyy-MM-dd}: historical targets were not saved by the old generator; this week is excluded from fairness history.");
                continue;
            }
            var snapshot = JsonSerializer.Deserialize<RosterPlanResponse>(historicalPlan.SnapshotJson)
                ?? throw new RosterInputException($"The saved fairness history for week {historicalPlan.WeekStart:yyyy-MM-dd} could not be read.");
            history.AddRange(snapshot.Employees.Where(e => ids.Contains(e.EmployeeId))
                .Select(e => new RosterSolverHistory(e.EmployeeId, historicalPlan.WeekStart, e.ScheduledHours, e.TargetHours)));
        }
        var input = new RosterSolverInput(weekStart, employees, availability, demand, boundaries, RosterSettingsService.ToOptions(settings), history);
        // Scalars only: stable fingerprint catches changed availability, targets, demand or adjacent rosters before saving.
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            DemandId = plan.Id, plan.UpdatedAtUtc, demand,
            Employees = employees.Select(e => new { e.Id, e.FirstName, e.LastName, e.TargetHours, e.CanWorkAlone }),
            Availability = availability.Select(s => new { s.EmployeeId, s.Date, s.StartTime, s.FinishTime }),
            boundaries, settings, history
        }))));
        return new LoadedRosterInput(input, settings, fingerprint, warnings);
    }
}
