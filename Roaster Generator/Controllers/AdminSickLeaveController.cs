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
[Authorize(Policy = AuthorizationPolicies.Manager)]
[Route("api/admin/sick-leave")]
public sealed class AdminSickLeaveController(
    AppDbContext db,
    UserManager<ApplicationUser> userManager) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var requests = await db.SickLeaveRequests.AsNoTracking()
            .OrderBy(request => request.Status)
            .ThenBy(request => request.StartDate)
            .ThenBy(request => request.Employee.LastName)
            .Select(SickLeaveResponseMapper.Projection)
            .ToListAsync(cancellationToken);

        return Ok(new SickLeaveOverviewResponse
        {
            Requested = requests.Where(request => request.Status == SickLeaveStatus.Requested)
                .ToList(),
            Reviewed = requests.Where(request => request.Status != SickLeaveStatus.Requested)
                .OrderByDescending(request => request.ReviewedAtUtc)
                .ToList()
        });
    }

    [HttpGet("{requestId:guid}/attachment")]
    public async Task<IActionResult> Download(Guid requestId, CancellationToken cancellationToken)
    {
        var request = await db.SickLeaveRequests.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == requestId && item.Status == SickLeaveStatus.Requested,
            cancellationToken);
        if (request?.AttachmentContent is null || request.AttachmentContentType is null || request.AttachmentFileName is null)
            return NotFound(new { message = "The sick note is no longer available." });

        return File(request.AttachmentContent, request.AttachmentContentType, request.AttachmentFileName);
    }

    [HttpPost("{requestId:guid}/approve")]
    public Task<IActionResult> Approve(Guid requestId, CancellationToken cancellationToken) =>
        ReviewAsync(requestId, SickLeaveStatus.Approved, null, cancellationToken);

    [HttpPost("{requestId:guid}/reject")]
    public Task<IActionResult> Reject(Guid requestId, [FromBody] SickLeaveReviewRequest request,
        CancellationToken cancellationToken) =>
        ReviewAsync(requestId, SickLeaveStatus.Rejected, request.Reason, cancellationToken);

    private async Task<IActionResult> ReviewAsync(Guid requestId, SickLeaveStatus status, string? reason,
        CancellationToken cancellationToken)
    {
        var sickLeave = await db.SickLeaveRequests.Include(request => request.Employee)
            .SingleOrDefaultAsync(request => request.Id == requestId && request.Status == SickLeaveStatus.Requested,
                cancellationToken);
        if (sickLeave is null) return NotFound(new { message = "Pending sick leave request not found." });

        var reviewer = await userManager.GetUserAsync(User);
        var reviewerName = "Manager";
        if (User.IsInRole(RoleNames.Admin))
        {
            reviewerName = "Admin";
        }
        else if (reviewer?.EmployeeId is Guid reviewerEmployeeId)
        {
            var managerName = await db.Employees.AsNoTracking()
                .Where(employee => employee.Id == reviewerEmployeeId)
                .Select(employee => new { employee.FirstName, employee.LastName })
                .SingleOrDefaultAsync(cancellationToken);
            if (managerName is not null)
            {
                reviewerName = $"{managerName.FirstName} {managerName.LastName}".Trim();
            }
        }
        var now = DateTimeOffset.UtcNow;
        sickLeave.Status = status;
        sickLeave.ReviewedAtUtc = now;
        sickLeave.ReviewedByUserId = reviewer?.Id;
        sickLeave.ReviewedByName = reviewerName;
        sickLeave.RejectionReason = status == SickLeaveStatus.Rejected
            ? NormalizeReason(reason)
            : null;
        sickLeave.AttachmentContent = null;
        sickLeave.AttachmentFileName = null;
        sickLeave.AttachmentContentType = null;
        sickLeave.AttachmentFileSize = null;
        sickLeave.AttachmentDeletedAtUtc = now;
        sickLeave.UpdatedAtUtc = now;
        sickLeave.Revision++;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "This sick leave request has already been reviewed." });
        }

        return Ok(SickLeaveResponseMapper.ToResponse(sickLeave));
    }

    private static string? NormalizeReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return null;
        var trimmed = reason.Trim();
        return trimmed.Length <= 500 ? trimmed : trimmed[..500];
    }
}
