using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Roaster_Generator.Contracts.Absent;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Security;
using Roaster_Generator.Services;

namespace Roaster_Generator.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.Manager)]
[Route("api/admin/absent")]
public sealed class AdminAbsentController(
    AbsentFormService forms,
    AbsentPdfService pdf,
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    IValidator<AbsentFormUpdateRequest> validator,
    ApplicationEventPublisher? events = null) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (await GetCurrentManagementNameAsync(ct) is null) return Forbid();
        return Ok(await forms.GetForManagementAsync(ct));
    }

    [HttpGet("{formId:guid}/pdf")]
    public async Task<IActionResult> DownloadPdf(Guid formId, CancellationToken ct)
    {
        var managerName = await GetCurrentManagementNameAsync(ct);
        if (managerName is null) return Forbid();
        var form = await forms.GetForManagementAsync(formId, ct);
        if (form is null) return NotFound(new { message = "Absent form not found." });

        var result = await pdf.CreateAsync(form, managerName, ct);
        return File(result.Content, "application/pdf", result.FileName);
    }

    [HttpPut("{formId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    public async Task<IActionResult> Update(Guid formId, [FromBody] AbsentFormUpdateRequest request,
        CancellationToken ct)
    {
        if (await GetCurrentManagementNameAsync(ct) is null) return Forbid();
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return ValidationError(ToErrors(validation), "The absent form is invalid.");

        var result = await forms.UpdateForAdminAsync(formId, request, ct);
        if (result.ShiftWasInvalid)
            return ValidationError(new Dictionary<string, string[]>
            {
                ["savedRosterShiftId"] = ["Select a saved driver-roster shift belonging to this driver, enter the shift manually, or keep the recorded shift."]
            }, "The absent form is invalid.");
        if (result.Form is null) return NotFound(new { message = "Absent form not found." });

        if (events is not null) await events.AbsentChangedAsync(result.Form.Id, result.Form.EmployeeId, ct);
        return Ok(result.Form);
    }

    [HttpDelete("{formId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    public async Task<IActionResult> Delete(Guid formId, CancellationToken ct)
    {
        if (await GetCurrentManagementNameAsync(ct) is null) return Forbid();
        var employeeId = await forms.DeleteForAdminAsync(formId, ct);
        if (employeeId is null) return NotFound(new { message = "Absent form not found." });
        if (events is not null) await events.AbsentChangedAsync(formId, employeeId.Value, ct);
        return NoContent();
    }

    private async Task<string?> GetCurrentManagementNameAsync(CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return null;
        if (user.EmployeeId is not Guid employeeId) return user.UserName?.Trim();

        var employee = await db.Employees.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == employeeId && item.IsActive, ct);
        return employee is null ? null : $"{employee.FirstName} {employee.LastName}".Trim();
    }
}
