using System.Data;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Roaster_Generator.Contracts.SickLeave;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Enums;
using Roaster_Generator.Security;
using Roaster_Generator.Services;

namespace Roaster_Generator.Controllers;

[ApiController]
[Authorize]
[Route("api/sick-leave")]
public sealed class SickLeaveController(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    IValidator<SickLeaveCreateRequest> validator) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var employee = await GetCurrentEmployeeAsync(cancellationToken);
        if (employee is null) return Forbid();

        var requests = await db.SickLeaveRequests.AsNoTracking()
            .Where(request => request.EmployeeId == employee.Id)
            .OrderByDescending(request => request.CreatedAtUtc)
            .Select(SickLeaveResponseMapper.Projection)
            .ToListAsync(cancellationToken);

        return Ok(new SickLeaveOverviewResponse
        {
            Requested = requests.Where(request => request.Status == SickLeaveStatus.Requested).ToList(),
            Reviewed = requests.Where(request => request.Status != SickLeaveStatus.Requested).ToList()
        });
    }

    [HttpPost]
    [RequestSizeLimit(SickNoteFileValidator.MaxFileSize + 64 * 1024)]
    public async Task<IActionResult> Create(
        [FromForm] SickLeaveCreateRequest request,
        CancellationToken cancellationToken)
    {
        var employee = await GetCurrentEmployeeAsync(cancellationToken);
        if (employee is null) return Forbid();

        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
            return ValidationError(ToErrors(validationResult), "The sick leave request is invalid.");

        ValidatedSickNote note;
        try
        {
            note = await SickNoteFileValidator.ReadAsync(request.File!, cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            return ValidationError(new Dictionary<string, string[]> { ["file"] = [exception.Message] },
                "The sick leave request is invalid.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (await HasOverlapAsync(employee.Id, request.StartDate, request.FinishDate, cancellationToken))
        {
            return Conflict(new { message = "This sick leave overlaps another requested or approved sick leave." });
        }

        var now = DateTimeOffset.UtcNow;
        var sickLeave = new SickLeaveRequest
        {
            EmployeeId = employee.Id,
            Employee = employee,
            StartDate = request.StartDate,
            FinishDate = request.FinishDate,
            Status = SickLeaveStatus.Requested,
            AttachmentContent = note.Content,
            AttachmentFileName = note.FileName,
            AttachmentContentType = note.ContentType,
            AttachmentFileSize = note.Content.LongLength,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.SickLeaveRequests.Add(sickLeave);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Conflict(new { message = "The sick leave request could not be created because the data changed. Please try again." });
        }

        return Ok(SickLeaveResponseMapper.ToResponse(sickLeave));
    }

    [HttpGet("{requestId:guid}/attachment")]
    public async Task<IActionResult> Download(Guid requestId, CancellationToken cancellationToken)
    {
        var employee = await GetCurrentEmployeeAsync(cancellationToken);
        if (employee is null) return Forbid();

        var request = await db.SickLeaveRequests.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == requestId && item.EmployeeId == employee.Id,
            cancellationToken);
        if (request?.AttachmentContent is null || request.AttachmentContentType is null || request.AttachmentFileName is null)
            return NotFound(new { message = "The sick note is no longer available." });

        return File(request.AttachmentContent, request.AttachmentContentType, request.AttachmentFileName);
    }

    [HttpDelete("{requestId:guid}")]
    public async Task<IActionResult> Delete(Guid requestId, CancellationToken cancellationToken)
    {
        var employee = await GetCurrentEmployeeAsync(cancellationToken);
        if (employee is null) return Forbid();

        var request = await db.SickLeaveRequests.SingleOrDefaultAsync(
            item => item.Id == requestId && item.EmployeeId == employee.Id && item.Status == SickLeaveStatus.Requested,
            cancellationToken);
        if (request is null) return NotFound(new { message = "Pending sick leave request not found." });

        db.SickLeaveRequests.Remove(request);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private Task<bool> HasOverlapAsync(Guid employeeId, DateOnly startDate, DateOnly finishDate,
        CancellationToken cancellationToken) =>
        db.SickLeaveRequests.AnyAsync(item =>
            item.EmployeeId == employeeId &&
            (item.Status == SickLeaveStatus.Requested || item.Status == SickLeaveStatus.Approved) &&
            item.StartDate <= finishDate && item.FinishDate >= startDate,
            cancellationToken);

    private async Task<Employee?> GetCurrentEmployeeAsync(CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user?.EmployeeId is not Guid employeeId) return null;
        var roles = await userManager.GetRolesAsync(user);
        if (!roles.Any(RoleNames.IsEmployeeRole)) return null;
        return await db.Employees.SingleOrDefaultAsync(
            employee => employee.Id == employeeId && employee.IsActive, cancellationToken);
    }

}
