using Microsoft.EntityFrameworkCore;
using Roaster_Generator.Data;

namespace Roaster_Generator.Services;

internal static class RosterBoundaryShifts
{
    public static async Task<IReadOnlyList<RosterSolverBoundaryShift>> LoadAsync(AppDbContext db,
        DateOnly weekStart, string rosterKind, Guid[] employeeIds, CancellationToken ct)
    {
        var weekEnd = weekStart.AddDays(7);
        // Include all saved drivers: an inactive driver or one without availability
        // this week may still occupy a company vehicle across the week boundary.
        var shifts = await db.RosterShifts.AsNoTracking()
            .Include(s => s.Employee).ThenInclude(e => e.DriverProfile)
            .Where(s => (rosterKind == RosterKinds.Drivers || employeeIds.Contains(s.EmployeeId)) &&
                s.Date >= weekStart.AddDays(-3) && s.Date < weekEnd.AddDays(3) &&
                s.RosterPlan.RosterKind == rosterKind && (s.Date < weekStart || s.Date >= weekEnd))
            .OrderBy(s => s.EmployeeId).ThenBy(s => s.Date).ThenBy(s => s.StartTime).ToListAsync(ct);
        return shifts.Select(shift =>
        {
            var start = shift.StartTime.Hour < 6 ? shift.StartTime.Hour + 24 : shift.StartTime.Hour;
            var finish = shift.FinishTime.Hour;
            while (finish <= start) finish += 24;
            return new RosterSolverBoundaryShift(shift.EmployeeId,
                shift.Date.ToDateTime(TimeOnly.MinValue).AddHours(start),
                shift.Date.ToDateTime(TimeOnly.MinValue).AddHours(finish),
                rosterKind == RosterKinds.Drivers ? CompanyVehicleFleet.RequiredBy(shift.Employee) : null);
        }).ToArray();
    }
}
