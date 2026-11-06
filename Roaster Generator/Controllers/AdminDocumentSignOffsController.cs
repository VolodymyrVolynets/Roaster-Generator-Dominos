using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Roaster_Generator.Contracts.Documents;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Security;
using Roaster_Generator.Services;

namespace Roaster_Generator.Controllers;

/// <summary>
/// Forms waiting for a manager's signature. This is the only access Managers have to other
/// employees' documents: a form disappears from here as soon as it has been signed.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.Manager)]
[Route("api/admin/document-signoffs")]
public sealed class AdminDocumentSignOffsController(
    DocumentSigningService signing,
    UserManager<ApplicationUser> userManager,
    AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await signing.ListSignOffsAsync(ct));

    [HttpGet("automatic-signature")]
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    public async Task<IActionResult> GetAutomaticSignature(CancellationToken ct) =>
        Ok(await signing.GetAutomaticSignatureAsync(ct));

    [HttpPut("automatic-signature")]
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<IActionResult> SaveAutomaticSignature([FromBody] DocumentSignOffRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await signing.SaveAutomaticSignatureAsync(await CurrentManagerAsync(ct), request, ct));
        }
        catch (DocumentSettingsException exception)
        {
            return ValidationError(new Dictionary<string, string[]> { ["signature"] = [exception.Message] }, "The signature could not be saved.");
        }
        catch (DocumentUnavailableException exception)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = exception.Message });
        }
    }

    [HttpDelete("automatic-signature")]
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    public async Task<IActionResult> RemoveAutomaticSignature(CancellationToken ct) =>
        await signing.RemoveAutomaticSignatureAsync(ct) ? NoContent() : NotFound(new { message = "No signature is saved." });

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        await signing.GetSignOffAsync(id, await CurrentManagerAsync(ct), ct) is { } form ? Ok(form) : SignOffNotFound();

    [HttpGet("{id:guid}/form")]
    public async Task<IActionResult> ReadForm(Guid id, CancellationToken ct)
    {
        if (await signing.ReadSignOffPdfAsync(id, await CurrentManagerAsync(ct), ct) is not { } content) return SignOffNotFound();
        Response.Headers.CacheControl = "no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(content, "application/pdf");
    }

    [HttpPost("{id:guid}")]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<IActionResult> Sign(Guid id, [FromBody] DocumentSignOffRequest request, CancellationToken ct)
    {
        try
        {
            return await signing.ManagerSignAsync(id, await CurrentManagerAsync(ct), request, ct) ? NoContent() : SignOffNotFound();
        }
        catch (DocumentSettingsException exception)
        {
            return ValidationError(new Dictionary<string, string[]> { ["signature"] = [exception.Message] }, "The form could not be signed.");
        }
    }

    private async Task<DocumentSignOffActor> CurrentManagerAsync(CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(User);
        var employee = user?.EmployeeId is Guid employeeId
            ? await db.Employees.AsNoTracking().SingleOrDefaultAsync(item => item.Id == employeeId, ct)
            : null;
        var userName = user?.UserName ?? "Unknown";
        var displayName = employee is null ? userName : $"{employee.FirstName} {employee.LastName}".Trim();
        return new DocumentSignOffActor(new DocumentActor(user?.Id, userName, User.IsInRole(RoleNames.Admin)), user?.EmployeeId, displayName);
    }

    private NotFoundObjectResult SignOffNotFound() => NotFound(new { message = "This form isn't waiting for a signature." });
}
