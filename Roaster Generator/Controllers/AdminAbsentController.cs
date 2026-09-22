using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Roaster_Generator.Contracts.Absent;
using Roaster_Generator.Security;
using Roaster_Generator.Services;

namespace Roaster_Generator.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.Admin)]
[Route("api/admin/absent")]
public sealed class AdminAbsentController(
    AbsentFormService forms,
    IValidator<AbsentFormUpdateRequest> validator,
    ApplicationEventPublisher? events = null) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) => Ok(await forms.GetForAdminAsync(ct));

    [HttpPut("{formId:guid}")]
    public async Task<IActionResult> Update(Guid formId, [FromBody] AbsentFormUpdateRequest request,
        CancellationToken ct)
    {
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
    public async Task<IActionResult> Delete(Guid formId, CancellationToken ct)
    {
        var employeeId = await forms.DeleteForAdminAsync(formId, ct);
        if (employeeId is null) return NotFound(new { message = "Absent form not found." });
        if (events is not null) await events.AbsentChangedAsync(formId, employeeId.Value, ct);
        return NoContent();
    }
}
