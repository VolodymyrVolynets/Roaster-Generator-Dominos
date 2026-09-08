using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Roaster_Generator.Contracts.Holidays;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Enums;
using Roaster_Generator.Security;
using Roaster_Generator.Services;

namespace Roaster_Generator.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.Admin)]
[Route("api/admin/holidays")]
public sealed class AdminHolidaysController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var requests = await QueryRequests(cancellationToken);
        return Ok(ToOverview(requests));
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export(CancellationToken cancellationToken)
    {
        var requests = await QueryRequests(cancellationToken);
        var csv = new StringBuilder();
        csv.AppendLine("Employee number,Employee name,Payroll number,Holiday hours,Status,Created at UTC,Updated at UTC,Used at UTC");

        foreach (var request in requests)
        {
            csv.AppendLine(string.Join(',',
                Csv(request.Employee.EmployeeNumber),
                Csv($"{request.Employee.FirstName} {request.Employee.LastName}".Trim()),
                Csv(request.Employee.PayrollNumber),
                request.Hours.ToString(CultureInfo.InvariantCulture),
                Csv(request.Status.ToString()),
                Csv(request.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)),
                Csv(request.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture)),
                Csv(request.UsedAtUtc?.ToString("O", CultureInfo.InvariantCulture))));
        }

        var content = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return File(content, "text/csv; charset=utf-8", $"holiday-requests-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv");
    }

    [HttpPost("{holidayId:guid}/approve")]
    public async Task<IActionResult> Approve(
        Guid holidayId,
        CancellationToken cancellationToken)
    {
        var holiday = await db.HolidayRequests
            .Include(request => request.Employee)
            .SingleOrDefaultAsync(
                request => request.Id == holidayId && request.Status == HolidayStatus.Requested,
                cancellationToken);
        if (holiday is null)
        {
            return NotFound(new { message = "Requested holiday not found." });
        }

        MarkUsed(holiday);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(HolidayResponseMapper.ToResponse(holiday));
    }

    [HttpPost("approve-all")]
    public async Task<IActionResult> ApproveAll(CancellationToken cancellationToken)
    {
        var holidays = await db.HolidayRequests
            .Where(request => request.Status == HolidayStatus.Requested)
            .ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        foreach (var holiday in holidays)
        {
            holiday.Status = HolidayStatus.Used;
            holiday.UsedAtUtc = now;
            holiday.UpdatedAtUtc = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Ok(new HolidayApprovalResponse { ApprovedCount = holidays.Count });
    }

    private async Task<List<HolidayRequest>> QueryRequests(CancellationToken cancellationToken) =>
        await db.HolidayRequests
            .AsNoTracking()
            .Include(request => request.Employee)
            .OrderBy(request => request.Status)
            .ThenBy(request => request.Employee.LastName)
            .ThenBy(request => request.Employee.FirstName)
            .ThenByDescending(request => request.UpdatedAtUtc)
            .ToListAsync(cancellationToken);

    private static AdminHolidayResponse ToOverview(IReadOnlyCollection<HolidayRequest> requests) => new()
    {
        Requested = requests
            .Where(request => request.Status == HolidayStatus.Requested)
            .Select(HolidayResponseMapper.ToResponse)
            .ToList(),
        Used = requests
            .Where(request => request.Status == HolidayStatus.Used)
            .Select(HolidayResponseMapper.ToResponse)
            .ToList()
    };

    private static void MarkUsed(HolidayRequest holiday)
    {
        holiday.Status = HolidayStatus.Used;
        holiday.UsedAtUtc = DateTimeOffset.UtcNow;
        holiday.UpdatedAtUtc = holiday.UsedAtUtc.Value;
    }

    private static string Csv(string? value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
}
