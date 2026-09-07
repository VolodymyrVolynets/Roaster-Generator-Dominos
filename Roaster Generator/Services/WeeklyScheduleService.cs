using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Roaster_Generator.Contracts.Schedules;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;

namespace Roaster_Generator.Services;

public sealed class WeeklyScheduleService(AppDbContext db)
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

        return BuildResponse(employee, weekStart, shifts);
    }

    public async Task<WeeklyAvailabilityResponse> GetWeekForAllAsync(
        int weekOffset,
        CancellationToken cancellationToken)
    {
        var weekStart = GetWeekMonday(weekOffset);
        var weekEnd = weekStart.AddDays(7);

        var employees = await db.Employees
            .AsNoTracking()
            .Where(employee => employee.IsActive)
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

        var schedules = employees
            .Select(employee => shiftsByEmployee.TryGetValue(employee.Id, out var employeeShifts)
                ? BuildResponse(employee, weekStart, employeeShifts)
                : BuildResponse(employee, weekStart, []))
            .ToList();

        return new WeeklyAvailabilityResponse
        {
            WeekStart = weekStart,
            WeekEnd = weekStart.AddDays(6),
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

        return BuildResponse(employee, weekStart, savedShifts);
    }

    public static DateOnly GetCurrentWeekMonday()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var daysSinceMonday = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;

        return today.AddDays(-daysSinceMonday);
    }

    public static DateOnly GetWeekMonday(int weekOffset) =>
        GetCurrentWeekMonday().AddDays(weekOffset * 7);

    private static WeeklyScheduleResponse BuildResponse(
        Employee employee,
        DateOnly weekStart,
        IReadOnlyCollection<Shift> shifts)
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
            Days = days
        };
    }

    private static string? FormatTime(TimeOnly? time) =>
        time?.ToString("HH:mm", CultureInfo.InvariantCulture);
}
