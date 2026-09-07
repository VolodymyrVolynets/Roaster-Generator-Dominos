using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Roaster_Generator.Contracts.Schedules;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;

namespace Roaster_Generator.Services;

public sealed class WeeklyScheduleService(AppDbContext db)
{
    public async Task<WeeklyScheduleResponse?> GetNextWeekAsync(
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var employee = await db.Employees
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == employeeId, cancellationToken);

        if (employee is null)
        {
            return null;
        }

        var weekStart = GetNextWeekMonday();
        var shifts = await db.Shifts
            .AsNoTracking()
            .Where(shift => shift.EmployeeId == employeeId)
            .Where(shift => shift.Date >= weekStart && shift.Date < weekStart.AddDays(7))
            .OrderBy(shift => shift.Date)
            .ToListAsync(cancellationToken);

        return BuildResponse(employee, weekStart, shifts);
    }

    public async Task<WeeklyScheduleResponse?> ReplaceNextWeekAsync(
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

        var weekStart = GetNextWeekMonday();
        ValidateRequest(request, weekStart);

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

    public static DateOnly GetNextWeekMonday()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var daysUntilNextMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;

        if (daysUntilNextMonday == 0)
        {
            daysUntilNextMonday = 7;
        }

        return today.AddDays(daysUntilNextMonday);
    }

    private static void ValidateRequest(WeeklyScheduleRequest request, DateOnly weekStart)
    {
        if (request.Days.Count != 7)
        {
            throw new ScheduleValidationException("Exactly seven days are required.");
        }

        for (var index = 0; index < request.Days.Count; index++)
        {
            var day = request.Days[index];
            var expectedDate = weekStart.AddDays(index);

            if (day.Date != expectedDate)
            {
                throw new ScheduleValidationException(
                    $"Schedule dates must cover {weekStart:yyyy-MM-dd} through {weekStart.AddDays(6):yyyy-MM-dd}.");
            }

            if (day.StartTime is null && day.FinishTime is null)
            {
                continue;
            }

            if (day.StartTime is null || day.FinishTime is null)
            {
                throw new ScheduleValidationException(
                    $"Both start and finish times are required for {day.Date:yyyy-MM-dd}.");
            }

            if (day.FinishTime <= day.StartTime)
            {
                throw new ScheduleValidationException(
                    $"Finish time must be after start time for {day.Date:yyyy-MM-dd}.");
            }
        }
    }

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

public sealed class ScheduleValidationException(string message) : Exception(message);
