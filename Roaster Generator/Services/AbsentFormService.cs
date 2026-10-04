using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Roaster_Generator.Contracts.Absent;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;

namespace Roaster_Generator.Services;

public sealed class AbsentFormService(AppDbContext db, TimeProvider timeProvider)
{
    private static readonly TimeZoneInfo ShopTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Dublin");

    public DateOnly CurrentNotificationDate => GetShopDate(timeProvider.GetUtcNow());

    public async Task<DriverAbsentOverviewResponse> GetForDriverAsync(Employee employee, CancellationToken ct)
    {
        var shifts = await db.RosterShifts.AsNoTracking()
            .Where(shift => shift.EmployeeId == employee.Id && shift.RosterPlan.RosterKind == RosterKinds.Drivers)
            .OrderByDescending(shift => shift.Date).ThenBy(shift => shift.StartTime)
            .ToListAsync(ct);
        var forms = await OrderedForms().Where(form => form.EmployeeId == employee.Id).ToListAsync(ct);
        return new DriverAbsentOverviewResponse
        {
            DriverFullName = FullName(employee),
            PayrollNumber = employee.PayrollNumber,
            NotificationDate = CurrentNotificationDate,
            Shifts = shifts.Select(shift => MapShift(shift.Id, shift.Date, shift.StartTime, shift.FinishTime)).ToList(),
            Forms = forms.Select(ToResponse).ToList()
        };
    }

    public async Task<IReadOnlyList<DriverAbsentGroupResponse>> GetForManagementAsync(
        AbsentManagementFilter filter, CancellationToken ct)
    {
        var forms = await OrderedForms().ToListAsync(ct);
        var employeeName = filter.EmployeeName?.Trim();
        forms = forms
            .Where(form => string.IsNullOrWhiteSpace(employeeName) ||
                           form.DriverFullName.Contains(employeeName, StringComparison.OrdinalIgnoreCase))
            .Where(form => !filter.ShiftStartDate.HasValue ||
                           GetShiftStartDate(form) >= filter.ShiftStartDate.Value)
            .Where(form => !filter.ShiftFinishDate.HasValue ||
                           GetShiftFinishDate(form) <= filter.ShiftFinishDate.Value)
            .ToList();
        var employeeIds = forms.Select(form => form.EmployeeId).Distinct().ToList();
        var shifts = await db.RosterShifts.AsNoTracking()
            .Where(shift => employeeIds.Contains(shift.EmployeeId) &&
                            shift.RosterPlan.RosterKind == RosterKinds.Drivers)
            .OrderByDescending(shift => shift.Date).ThenBy(shift => shift.StartTime)
            .ToListAsync(ct);
        return forms.GroupBy(form => form.EmployeeId)
            .Select(group => new DriverAbsentGroupResponse
            {
                EmployeeId = group.Key,
                DriverFullName = group.First().DriverFullName,
                Shifts = shifts.Where(shift => shift.EmployeeId == group.Key)
                    .Select(shift => MapShift(shift.Id, shift.Date, shift.StartTime, shift.FinishTime)).ToList(),
                Forms = group.Select(ToResponse).ToList()
            })
            .OrderBy(group => group.DriverFullName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.EmployeeId).ToList();
    }

    public async Task<AbsentFormResponse?> GetForManagementAsync(Guid formId, CancellationToken ct)
    {
        var form = await OrderedForms().SingleOrDefaultAsync(item => item.Id == formId, ct);
        return form is null ? null : ToResponse(form);
    }

    public async Task<AbsentFormResponse?> SubmitAsync(Employee employee, AbsentFormCreateRequest request,
        CancellationToken ct)
    {
        RosterShift? shift = null;
        if (request.SavedRosterShiftId is Guid shiftId)
        {
            shift = await db.RosterShifts.AsNoTracking().SingleOrDefaultAsync(item =>
                item.Id == shiftId && item.EmployeeId == employee.Id &&
                item.RosterPlan.RosterKind == RosterKinds.Drivers, ct);
            if (shift is null) return null;
        }

        var now = timeProvider.GetUtcNow();
        var form = new AbsentForm
        {
            EmployeeId = employee.Id,
            DriverFullName = FullName(employee),
            PayrollNumber = employee.PayrollNumber,
            SavedRosterShiftId = shift?.Id,
            ShiftDate = shift?.Date ?? request.ShiftDate!.Value,
            ShiftStartTime = shift?.StartTime ?? ParseHour(request.ShiftStartTime!),
            ShiftFinishTime = shift?.FinishTime ?? ParseHour(request.ShiftFinishTime!),
            NotificationDate = request.NotificationDate,
            NotificationTime = TimeOnly.ParseExact(request.NotificationTime, "HH:mm", CultureInfo.InvariantCulture),
            NotificationMethod = request.NotificationMethod.Trim(),
            CancellationReason = request.CancellationReason.Trim(),
            SubmittedAtUtc = now
        };
        db.AbsentForms.Add(form);
        await db.SaveChangesAsync(ct);
        return ToResponse(form);
    }

    public async Task<AbsentFormUpdateResult> UpdateForAdminAsync(Guid formId, AbsentFormUpdateRequest request,
        CancellationToken ct)
    {
        var form = await db.AbsentForms.SingleOrDefaultAsync(item => item.Id == formId, ct);
        if (form is null) return new AbsentFormUpdateResult(null, false);

        if (request.SavedRosterShiftId is Guid shiftId)
        {
            var shift = await db.RosterShifts.AsNoTracking().SingleOrDefaultAsync(item =>
                item.Id == shiftId && item.EmployeeId == form.EmployeeId &&
                item.RosterPlan.RosterKind == RosterKinds.Drivers, ct);
            if (shift is null) return new AbsentFormUpdateResult(null, true);
            form.SavedRosterShiftId = shift.Id;
            form.ShiftDate = shift.Date;
            form.ShiftStartTime = shift.StartTime;
            form.ShiftFinishTime = shift.FinishTime;
        }
        else if (request.ShiftDate is DateOnly shiftDate)
        {
            form.SavedRosterShiftId = null;
            form.ShiftDate = shiftDate;
            form.ShiftStartTime = ParseHour(request.ShiftStartTime!);
            form.ShiftFinishTime = ParseHour(request.ShiftFinishTime!);
        }

        form.NotificationDate = request.NotificationDate;
        form.NotificationTime = TimeOnly.ParseExact(request.NotificationTime, "HH:mm", CultureInfo.InvariantCulture);
        form.NotificationMethod = request.NotificationMethod.Trim();
        form.CancellationReason = request.CancellationReason.Trim();
        await db.SaveChangesAsync(ct);
        return new AbsentFormUpdateResult(ToResponse(form), false);
    }

    public async Task<Guid?> DeleteForAdminAsync(Guid formId, CancellationToken ct)
    {
        var form = await db.AbsentForms.SingleOrDefaultAsync(item => item.Id == formId, ct);
        if (form is null) return null;
        var employeeId = form.EmployeeId;
        db.AbsentForms.Remove(form);
        await db.SaveChangesAsync(ct);
        return employeeId;
    }

    private IOrderedQueryable<AbsentForm> OrderedForms() => db.AbsentForms.AsNoTracking()
        .OrderByDescending(form => form.NotificationDate)
        .ThenByDescending(form => form.NotificationTime)
        .ThenByDescending(form => form.SubmittedAtUtc)
        .ThenBy(form => form.Id);

    private static DateOnly GetShopDate(DateTimeOffset now) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, ShopTimeZone).DateTime);

    private static DateOnly GetShiftStartDate(AbsentForm form) =>
        form.ShiftDate.AddDays(form.ShiftStartTime.Hour < 6 ? 1 : 0);

    private static DateOnly GetShiftFinishDate(AbsentForm form) =>
        form.ShiftDate.AddDays((form.ShiftStartTime.Hour < 6 ? 1 : 0) +
                               (form.ShiftFinishTime <= form.ShiftStartTime ? 1 : 0));

    private static string FullName(Employee employee) => $"{employee.FirstName} {employee.LastName}".Trim();

    private static TimeOnly ParseHour(string value) =>
        TimeOnly.ParseExact(value, "HH:mm", CultureInfo.InvariantCulture);

    private static AbsentShiftResponse MapShift(Guid? id, DateOnly date, TimeOnly start, TimeOnly finish) => new()
    {
        Id = id, Date = date,
        StartTime = start.ToString("HH:mm", CultureInfo.InvariantCulture),
        FinishTime = finish.ToString("HH:mm", CultureInfo.InvariantCulture),
        StartDayOffset = start.Hour < 6 ? 1 : 0,
        FinishDayOffset = (start.Hour < 6 ? 1 : 0) + (finish <= start ? 1 : 0)
    };

    private static AbsentFormResponse ToResponse(AbsentForm form) => new()
    {
        Id = form.Id, EmployeeId = form.EmployeeId,
        DriverFullName = form.DriverFullName, PayrollNumber = form.PayrollNumber,
        Shift = MapShift(form.SavedRosterShiftId, form.ShiftDate, form.ShiftStartTime, form.ShiftFinishTime),
        NotificationDate = form.NotificationDate,
        NotificationTime = form.NotificationTime.ToString("HH:mm", CultureInfo.InvariantCulture),
        NotificationMethod = form.NotificationMethod, CancellationReason = form.CancellationReason,
        SubmittedAtUtc = form.SubmittedAtUtc
    };
}

public sealed record AbsentFormUpdateResult(AbsentFormResponse? Form, bool ShiftWasInvalid);
