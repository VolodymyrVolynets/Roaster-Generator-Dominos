using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Roaster_Generator.Contracts.Schedules;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Enums;

namespace Roaster_Generator.Services;

public sealed class WeeklyScheduleService(AppDbContext db, RosterInputService? rosterInputs)
{
    public const int MinWeekOffset = 1;
    public const int MaxWeekOffset = 3;

    public async Task<WeeklyScheduleResponse?> GetWeekAsync(
        Guid employeeId,
        int weekOffset,
        CancellationToken cancellationToken)
    {
        var employee = await db.Employees
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == employeeId, cancellationToken);

        if (employee is null)
        {
            return null;
        }

        var weekStart = GetWeekMonday(weekOffset);
        var shifts = await db.Shifts
            .AsNoTracking()
            .Where(shift => shift.EmployeeId == employeeId)
            .Where(shift => shift.Date >= weekStart && shift.Date < weekStart.AddDays(7))
            .OrderBy(shift => shift.Date)
            .ToListAsync(cancellationToken);

        var isDriver = await DriverRosterEmployees.Query(db)
            .AnyAsync(item => item.Id == employeeId, cancellationToken);
        var approximateHours = isDriver
            ? (await GetApproximateHoursAsync(weekStart, cancellationToken)).GetValueOrDefault(employeeId)
            : null;
        var heatmap = isDriver ? await BuildHeatmapAsync(weekStart, cancellationToken) : null;

        return BuildResponse(employee, weekStart, shifts, heatmap, approximateHours);
    }

    public async Task<WeeklyAvailabilityResponse> GetWeekForAllAsync(
        int weekOffset,
        CancellationToken cancellationToken)
    {
        var weekStart = GetWeekMonday(weekOffset);
        var weekEnd = weekStart.AddDays(7);

        var employees = await DriverRosterEmployees.Query(db)
            .AsNoTracking()
            .OrderBy(employee => employee.LastName)
            .ThenBy(employee => employee.FirstName)
            .ToListAsync(cancellationToken);

        var employeeIds = employees.Select(employee => employee.Id).ToArray();
        var shifts = await db.Shifts
            .AsNoTracking()
            .Where(shift => employeeIds.Contains(shift.EmployeeId))
            .Where(shift => shift.Date >= weekStart && shift.Date < weekEnd)
            .OrderBy(shift => shift.Date)
            .ToListAsync(cancellationToken);

        var shiftsByEmployee = shifts
            .GroupBy(shift => shift.EmployeeId)
            .ToDictionary(group => group.Key, group => (IReadOnlyCollection<Shift>)group.ToList());

        var approximateHours = await GetApproximateHoursAsync(weekStart, cancellationToken);
        var schedules = employees
            .Select(employee => shiftsByEmployee.TryGetValue(employee.Id, out var employeeShifts)
                ? BuildResponse(employee, weekStart, employeeShifts,
                    approximateHours: approximateHours.GetValueOrDefault(employee.Id))
                : BuildResponse(employee, weekStart, [],
                    approximateHours: approximateHours.GetValueOrDefault(employee.Id)))
            .ToList();

        var heatmap = await BuildHeatmapAsync(weekStart, cancellationToken);

        return new WeeklyAvailabilityResponse
        {
            WeekStart = weekStart,
            WeekEnd = weekStart.AddDays(6),
            Heatmap = heatmap,
            Employees = schedules
        };
    }

    public async Task<WeeklyScheduleResponse?> ReplaceWeekAsync(
        Guid employeeId,
        WeeklyScheduleRequest request,
        CancellationToken cancellationToken)
    {
        var employee = await db.Employees
            .SingleOrDefaultAsync(item => item.Id == employeeId, cancellationToken);

        if (employee is null)
        {
            return null;
        }

        var weekStart = GetWeekMonday(request.WeekOffset);

        var weekEnd = weekStart.AddDays(7);
        var existingShifts = await db.Shifts
            .Where(shift => shift.EmployeeId == employeeId)
            .Where(shift => shift.Date >= weekStart && shift.Date < weekEnd)
            .ToListAsync(cancellationToken);

        db.Shifts.RemoveRange(existingShifts);

        foreach (var day in request.Days.Where(day => day.StartTime is not null))
        {
            db.Shifts.Add(new Shift
            {
                EmployeeId = employeeId,
                Date = day.Date,
                StartTime = day.StartTime!.Value,
                FinishTime = day.FinishTime!.Value
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        var savedShifts = await db.Shifts
            .AsNoTracking()
            .Where(shift => shift.EmployeeId == employeeId)
            .Where(shift => shift.Date >= weekStart && shift.Date < weekEnd)
            .OrderBy(shift => shift.Date)
            .ToListAsync(cancellationToken);

        var isDriver = await DriverRosterEmployees.Query(db)
            .AnyAsync(item => item.Id == employeeId, cancellationToken);
        var approximateHours = isDriver
            ? (await GetApproximateHoursAsync(weekStart, cancellationToken)).GetValueOrDefault(employeeId)
            : null;
        var heatmap = isDriver ? await BuildHeatmapAsync(weekStart, cancellationToken) : null;

        return BuildResponse(employee, weekStart, savedShifts, heatmap, approximateHours);
    }

    public static DateOnly GetCurrentWeekMonday()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var daysSinceMonday = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;

        return today.AddDays(-daysSinceMonday);
    }

    public static DateOnly GetWeekMonday(int weekOffset) =>
        GetCurrentWeekMonday().AddDays(checked(weekOffset * 7));

    public static bool IsValidWeekOffset(int weekOffset)
    {
        try
        {
            _ = GetWeekMonday(weekOffset);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static WeeklyScheduleResponse BuildResponse(
        Employee employee,
        DateOnly weekStart,
        IReadOnlyCollection<Shift> shifts,
        AvailabilityHeatmapResponse? heatmap = null,
        FairDriverHoursAllocation? approximateHours = null)
    {
        var shiftsByDate = shifts.ToDictionary(shift => shift.Date);

        var days = Enumerable.Range(0, 7)
            .Select(offset =>
            {
                var date = weekStart.AddDays(offset);
                shiftsByDate.TryGetValue(date, out var shift);

                return new ScheduleDayResponse
                {
                    Date = date,
                    DayOfWeek = date.DayOfWeek.ToString(),
                    StartTime = FormatTime(shift?.StartTime),
                    FinishTime = FormatTime(shift?.FinishTime)
                };
            })
            .ToList();

        return new WeeklyScheduleResponse
        {
            EmployeeId = employee.Id,
            EmployeeName = $"{employee.FirstName} {employee.LastName}".Trim(),
            WeekStart = weekStart,
            WeekEnd = weekStart.AddDays(6),
            ApproximateHours = approximateHours?.ExpectedHours,
            ApproximateCapacityHours = approximateHours?.CapacityHours,
            Heatmap = heatmap,
            Days = days
        };
    }

    private async Task<IReadOnlyDictionary<Guid, FairDriverHoursAllocation>> GetApproximateHoursAsync(
        DateOnly weekStart,
        CancellationToken cancellationToken)
    {
        if (rosterInputs is null) return new Dictionary<Guid, FairDriverHoursAllocation>();

        try
        {
            var loaded = await rosterInputs.LoadAsync(weekStart, cancellationToken, RosterKinds.Drivers);
            return loaded.Input.ExpectedHoursByEmployee
                ?? new Dictionary<Guid, FairDriverHoursAllocation>();
        }
        catch (RosterInputException)
        {
            // The heatmap can still guide availability while demand is missing or incomplete.
            return new Dictionary<Guid, FairDriverHoursAllocation>();
        }
    }

    private async Task<AvailabilityHeatmapResponse> BuildHeatmapAsync(
        DateOnly weekStart,
        CancellationToken cancellationToken)
    {
        var plan = await db.DemandPlans.AsNoTracking()
            .Include(item => item.Columns)
            .Include(item => item.Rows).ThenInclude(row => row.Values)
            .SingleOrDefaultAsync(item => item.WeekStart == weekStart && item.DemandKind == DemandKinds.Outside,
                cancellationToken);
        if (plan is null)
        {
            return new AvailabilityHeatmapResponse
            {
                Message = "Outside demand has not been entered for this week yet."
            };
        }

        var drivers = await DriverRosterEmployees.Query(db).AsNoTracking()
            .Select(employee => employee.Id)
            .ToListAsync(cancellationToken);
        var weekEnd = weekStart.AddDays(7);
        var availability = await db.Shifts.AsNoTracking()
            .Where(shift => drivers.Contains(shift.EmployeeId) && shift.Date >= weekStart && shift.Date < weekEnd)
            .ToListAsync(cancellationToken);
        var sickLeave = await db.SickLeaveRequests.AsNoTracking()
            .Where(request => drivers.Contains(request.EmployeeId) && request.Status == SickLeaveStatus.Approved &&
                request.StartDate < weekEnd && request.FinishDate >= weekStart)
            .Select(request => new { request.EmployeeId, request.StartDate, request.FinishDate })
            .ToListAsync(cancellationToken);
        var unavailableDates = sickLeave
            .SelectMany(request => Enumerable.Range(0, request.FinishDate.DayNumber - request.StartDate.DayNumber + 1)
                .Select(offset => (request.EmployeeId, Date: request.StartDate.AddDays(offset))))
            .ToHashSet();
        var windows = availability
            .Where(shift => !unavailableDates.Contains((shift.EmployeeId, shift.Date)))
            .GroupBy(shift => shift.EmployeeId)
            .ToDictionary(group => group.Key, group => group.Select(ToWindow).ToArray());
        var columnPositions = plan.Columns
            .Where(column => column.Position is >= 0 and < 7)
            .ToDictionary(column => column.Id, column => column.Position);
        var slots = plan.Rows
            .Where(row => row.Hour is >= 0 and < 24)
            .SelectMany(row => row.Values
                .Where(value => value.Demand is > 0 && columnPositions.ContainsKey(value.DemandColumnId))
                .Select(value => new
                {
                    Date = weekStart.AddDays(columnPositions[value.DemandColumnId]),
                    Hour = BusinessHour(row.Hour),
                    Required = value.Demand!.Value
                }))
            .OrderBy(slot => slot.Date)
            .ThenBy(slot => slot.Hour)
            .Select(slot =>
            {
                var available = drivers.Count(driverId => IsAvailable(
                    windows.GetValueOrDefault(driverId) ?? [], slot.Date, slot.Hour));
                var shortage = Math.Max(0, slot.Required - available);
                var level = shortage > 0
                    ? "shortage"
                    : available == slot.Required
                        ? "tight"
                        : available == slot.Required + 1
                            ? "limited"
                            : "covered";
                return new AvailabilityHeatmapSlotResponse
                {
                    Date = slot.Date,
                    DayOfWeek = slot.Date.DayOfWeek.ToString(),
                    Hour = slot.Hour,
                    StartTime = $"{slot.Hour % 24:00}:00",
                    StartDayOffset = slot.Hour / 24,
                    RequiredDrivers = slot.Required,
                    AvailableDrivers = available,
                    ShortageDrivers = shortage,
                    ScarcityScore = Math.Round(slot.Required / (double)Math.Max(available, 1), 2,
                        MidpointRounding.AwayFromZero),
                    Level = level
                };
            })
            .ToList();

        return new AvailabilityHeatmapResponse
        {
            DemandPlanExists = true,
            Message = slots.Count == 0
                ? "This week's outside demand does not currently require any drivers."
                : "Red hours are short of drivers; amber hours have little or no spare availability.",
            Slots = slots
        };
    }

    private static int BusinessHour(int hour) => hour < 6 ? hour + 24 : hour;

    private static AvailabilityWindow ToWindow(Shift shift)
    {
        var start = BusinessHour(shift.StartTime.Hour);
        var finish = shift.FinishTime.Hour;
        while (finish <= start) finish += 24;
        return new AvailabilityWindow(shift.Date, start, finish);
    }

    private static bool IsAvailable(IEnumerable<AvailabilityWindow> windows, DateOnly date, int hour) =>
        windows.Any(window => window.Date == date && window.StartHour <= hour && window.FinishHour > hour);

    private static string? FormatTime(TimeOnly? time) =>
        time?.ToString("HH:mm", CultureInfo.InvariantCulture);

    private sealed record AvailabilityWindow(DateOnly Date, int StartHour, int FinishHour);
}
