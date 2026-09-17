using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Roaster_Generator.Configuration;
using Roaster_Generator.Contracts.Roster;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Enums;
using Roaster_Generator.Security;

namespace Roaster_Generator.Services;

public sealed class RosterInputException(string message, IReadOnlyList<string>? diagnostics = null) : Exception(message)
{
    public IReadOnlyList<string> Diagnostics { get; } = diagnostics ?? [message];
}

public sealed record LoadedRosterInput(RosterSolverInput Input, RosterSettingsRequest Settings,
    string Fingerprint, IReadOnlyList<string> Warnings)
{
    public string? DemandFingerprint { get; init; }

    public string? AvailabilityFingerprint { get; init; }
}

public sealed class RosterInputService(AppDbContext db, RosterSettingsService settingsService,
    IOptions<ShopHoursOptions> shopHoursOptions,
    IFairDriverHoursCalculator? fairHoursCalculator = null)
{
    private readonly IFairDriverHoursCalculator fairHoursCalculator =
        fairHoursCalculator ?? new FairDriverHoursCalculator();

    public async Task<LoadedRosterInput> LoadAsync(DateOnly weekStart, CancellationToken ct, string rosterKind = RosterKinds.Drivers)
    {
        RosterKinds.EnsureEnabled(rosterKind);
        var demandKind = rosterKind == RosterKinds.Inside ? DemandKinds.Inside : DemandKinds.Outside;
        var employeeLabel = rosterKind == RosterKinds.Inside ? "inside employee" : "driver";
        var weekEnd = weekStart.AddDays(7);
        var plan = await db.DemandPlans.AsNoTracking().Include(p => p.Columns)
            .Include(p => p.Rows).ThenInclude(r => r.Values)
            .SingleOrDefaultAsync(p => p.WeekStart == weekStart && p.DemandKind == demandKind, ct)
            ?? throw new RosterInputException($"Enter {demandKind} demand for the week starting {weekStart:yyyy-MM-dd} before {(rosterKind == RosterKinds.Inside ? "creating" : "generating")} a roster.");
        if (plan.Columns.Count != 7 || !plan.Columns.Select(c => c.Position).Order().SequenceEqual(Enumerable.Range(0, 7)))
            throw new RosterInputException("The weekly demand plan must contain exactly Monday through Sunday.");
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
                r => r.Values.SingleOrDefault(v => v.DemandColumnId == column.Id));
            for (var hour = opening; hour < closing; hour++)
            {
                values.TryGetValue(hour % 24, out var value);
                var required = rosterKind == RosterKinds.Inside ? value?.InsideDemand : value?.Demand;
                if (required is null || required < 0)
                    diagnostics.Add($"{date.DayOfWeek} {hour % 24:00}:00{(hour >= 24 ? " (+1 day)" : "")}: enter a non-negative {employeeLabel} demand; this hour is missing or invalid.");
                else demand.Add(new RosterSolverDemand(date, hour, required.Value));
            }
            if (values.Any(v => (rosterKind == RosterKinds.Inside ? v.Value?.InsideDemand : v.Value?.Demand) > 0 &&
                                !Enumerable.Range(opening, closing - opening).Any(h => h % 24 == v.Key)))
                warnings.Add($"{date.DayOfWeek}: demand entries outside configured shop hours are excluded.");
        }
        if (diagnostics.Count > 0)
            throw new RosterInputException("The selected weekly demand plan is incomplete. Enter demand for every open hour (use 0 when no employees are needed).", diagnostics);

        var employeeQuery = rosterKind == RosterKinds.Inside
            ? InsideRosterEmployees.Query(db)
            : DriverRosterEmployees.Query(db);
        var employees = await employeeQuery
            .AsNoTracking()
            .Include(employee => employee.DriverProfile)
            .Include(employee => employee.InStoreProfile)
            .Include(employee => employee.ManagerProfile)
            .OrderBy(employee => employee.Id)
            .ToListAsync(ct);
        var ids = employees.Select(e => e.Id).ToArray();
        var roleMemberships = await db.UserRoles
            .Join(db.Roles, membership => membership.RoleId, role => role.Id,
                (membership, role) => new { membership.UserId, role.Name })
            .Join(db.Users.Where(user => user.EmployeeId.HasValue && ids.Contains(user.EmployeeId.Value)),
                membership => membership.UserId, user => user.Id,
                (membership, user) => new { EmployeeId = user.EmployeeId!.Value, Role = membership.Name })
            .ToListAsync(ct);
        var rolesByEmployee = roleMemberships.GroupBy(item => item.EmployeeId)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Role).ToHashSet());
        foreach (var employee in employees)
        {
            var roles = rolesByEmployee.GetValueOrDefault(employee.Id) ?? [];
            if (!roles.Contains(RoleNames.Driver)) employee.DriverProfile = null;
            if (!roles.Contains(RoleNames.InStore)) employee.InStoreProfile = null;
            if (!roles.Contains(RoleNames.Manager)) employee.ManagerProfile = null;
        }
        var approvedSickLeave = await db.SickLeaveRequests.AsNoTracking()
            .Where(request => ids.Contains(request.EmployeeId) &&
                              request.Status == SickLeaveStatus.Approved &&
                              request.StartDate < weekEnd && request.FinishDate >= weekStart)
            .Select(request => new { request.EmployeeId, request.StartDate, request.FinishDate })
            .ToListAsync(ct);
        var unavailableDates = approvedSickLeave
            .SelectMany(request => Enumerable.Range(0, request.FinishDate.DayNumber - request.StartDate.DayNumber + 1)
                .Select(offset => (request.EmployeeId, Date: request.StartDate.AddDays(offset))))
            .ToHashSet();
        var availability = await db.Shifts.AsNoTracking().Where(s => ids.Contains(s.EmployeeId) && s.Date >= weekStart && s.Date < weekEnd)
            .OrderBy(s => s.EmployeeId).ThenBy(s => s.Date).ThenBy(s => s.StartTime).ToListAsync(ct);
        availability = availability.Where(shift => !unavailableDates.Contains((shift.EmployeeId, shift.Date))).ToList();
        var boundaries = await RosterBoundaryShifts.LoadAsync(db, weekStart, rosterKind, ids, ct);
        var settings = await settingsService.GetAsync(ct);
        var options = RosterSettingsService.ToOptions(settings);
        var fairHours = fairHoursCalculator.Calculate(
            employees,
            availability,
            demand,
            settings.FairHoursAlpha,
            rosterKind: rosterKind,
            fleet: CompanyVehicleFleet.From(options),
            boundaries: boundaries);
        foreach (var employee in employees)
        {
            var sickDates = unavailableDates.Count(item => item.EmployeeId == employee.Id && item.Date >= weekStart && item.Date < weekEnd);
            if (sickDates > 0)
                warnings.Add($"{employee.FirstName} {employee.LastName}: approved sick leave removes availability on {sickDates} day{(sickDates == 1 ? string.Empty : "s")} this week.");
            if (!availability.Any(s => s.EmployeeId == employee.Id))
                warnings.Add($"{employee.FirstName} {employee.LastName}: no availability entered; approximate hours are 0.");
            else if (employee.MaximumWeeklyHours == 0)
                warnings.Add($"{employee.FirstName} {employee.LastName}: maximum weekly hours is 0; approximate hours are 0.");
            else if (fairHours.Drivers.GetValueOrDefault(employee.Id)?.ExpectedHours == 0)
                warnings.Add($"{employee.FirstName} {employee.LastName}: no hours can be allocated within demand, useful availability, vehicle and support limits; approximate hours are 0.");
        }
        if (fairHours.UnallocatedDemandHours > 0.001)
            warnings.Add($"Useful availability can receive {fairHours.TotalAllocatedHours:F1} of {fairHours.TotalDemandHours:F1} demanded {employeeLabel}-hours; {fairHours.UnallocatedDemandHours:F1} hours could not be allocated approximately.");
        var historyPlans = await db.RosterPlans.AsNoTracking()
            .Where(p => p.WeekStart >= weekStart.AddDays(-28) && p.WeekStart < weekStart && p.RosterKind == rosterKind)
            .OrderBy(p => p.WeekStart).Select(p => new { p.WeekStart, p.SnapshotJson }).ToListAsync(ct);
        var history = new List<RosterSolverHistory>();
        foreach (var historicalPlan in historyPlans)
        {
            if (historicalPlan.SnapshotJson is null)
            {
                warnings.Add($"Week {historicalPlan.WeekStart:yyyy-MM-dd}: automatically calculated hours were not saved by the old generator; this week is excluded from fairness history.");
                continue;
            }
            var snapshot = JsonSerializer.Deserialize<RosterPlanResponse>(historicalPlan.SnapshotJson)
                ?? throw new RosterInputException($"The saved fairness history for week {historicalPlan.WeekStart:yyyy-MM-dd} could not be read.");
            var automaticHistory = snapshot.Employees
                .Where(e => ids.Contains(e.EmployeeId) && e.ApproximateHours > 0)
                .Select(e => new RosterSolverHistory(e.EmployeeId, historicalPlan.WeekStart, e.ScheduledHours,
                    e.ApproximateHours, ReadHistoryShiftCount(e)))
                .ToArray();
            if (automaticHistory.Length == 0 && snapshot.Employees.Any(e => ids.Contains(e.EmployeeId)))
                warnings.Add($"Week {historicalPlan.WeekStart:yyyy-MM-dd}: the saved roster predates automatic approximate hours; this week is excluded from fairness history.");
            history.AddRange(automaticHistory);
        }
        var input = new RosterSolverInput(weekStart, employees, availability, demand, boundaries,
            options, history, rosterKind, fairHours.Drivers);
        var demandFingerprint = Fingerprint(demand);
        var availabilityFingerprint = Fingerprint(new
        {
            Employees = employees.Select(e => new
            {
                e.Id,
                e.MaximumWeeklyHours,
                Roles = RosterKinds.Roles(e),
                e.DriverProfile?.DriverType,
                IsOwn = rosterKind == RosterKinds.Drivers ? e.DriverProfile?.IsOwn : null
            }),
            Fleet = rosterKind == RosterKinds.Drivers ? CompanyVehicleFleet.From(options) : null,
            FleetReservations = rosterKind == RosterKinds.Drivers
                ? boundaries.Where(shift => shift.CompanyVehicleType.HasValue).ToArray() : [],
            Availability = availability.Select(s => new { s.EmployeeId, s.Date, s.StartTime, s.FinishTime }),
            ApproximateHours = fairHours.Drivers.Values.OrderBy(item => item.EmployeeId)
        });
        // Scalars only: stable fingerprint catches changed scheduling inputs before saving.
        var fingerprint = Fingerprint(new
        {
            rosterKind, demandFingerprint, availabilityFingerprint, boundaries, settings, history
        });
        return new LoadedRosterInput(input, settings, fingerprint, warnings)
        {
            DemandFingerprint = demandFingerprint,
            AvailabilityFingerprint = availabilityFingerprint
        };
    }

    // Older snapshots may have scheduled totals without usable per-shift durations.
    // Keep their shift count unknown so they cannot distort the historical average.
    public static int? ReadHistoryShiftCount(RosterEmployeeResponse employee)
    {
        if (employee.Shifts is null) return null;
        if (employee.Shifts.Count == 0) return employee.ScheduledHours == 0 ? 0 : null;
        return employee.Shifts.All(shift => shift is not null && shift.DurationHours > 0)
            && employee.Shifts.Sum(shift => (long)shift.DurationHours) == employee.ScheduledHours
                ? employee.Shifts.Count
                : null;
    }

    private static string Fingerprint<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));

}
