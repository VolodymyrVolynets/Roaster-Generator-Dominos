using System.Security.Cryptography;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Roaster_Generator.Contracts.Documents;
using Roaster_Generator.Entities;
using Roaster_Generator.Security;
using Roaster_Generator.Services;

namespace Roaster_Generator.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.Admin)]
[Route("api/admin/documents")]
public sealed class AdminDocumentsController(
    AdminDocumentService documents,
    UserManager<ApplicationUser> userManager,
    IValidator<DocumentAdminUpdateRequest> validator) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] AdminDocumentFilters filters, CancellationToken ct) =>
        Ok(await documents.ListAsync(filters, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        await documents.GetAsync(id, ct) is { } document ? Ok(document) : DocumentNotFound();

    /// <param name="preview">True when the file is shown on screen, so the audit trail records a view.</param>
    [HttpGet("{id:guid}/files/{fileId:guid}")]
    public async Task<IActionResult> ReadFile(Guid id, Guid fileId, [FromQuery] bool preview, CancellationToken ct)
    {
        AdminDocumentDownload? file;
        try
        {
            file = await documents.ReadFileAsync(id, fileId, preview, CurrentActor(), ct);
        }
        catch (CryptographicException)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "This file could not be decrypted." });
        }

        if (file is null) return NotFound(new { message = "File not found." });
        Response.Headers.CacheControl = "no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] DocumentAdminUpdateRequest request, CancellationToken ct)
    {
        var validationError = await ValidateRequestAsync(validator, request, "The document is invalid.", ct);
        if (validationError is not null) return validationError;

        try
        {
            return await documents.UpdateAsync(id, request, CurrentActor(), ct) is { } updated ? Ok(updated) : DocumentNotFound();
        }
        catch (DocumentConflictException)
        {
            return Conflict(new { message = "This document has changed. Reload it and try again." });
        }
    }

    [HttpPost("{id:guid}/request-signature")]
    public async Task<IActionResult> RequestSignature(Guid id, CancellationToken ct) =>
        await documents.RequestSignatureAsync(id, CurrentActor(), ct) is { } updated ? Ok(updated) : DocumentNotFound();

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        await documents.DeleteAsync(id, CurrentActor(), ct) ? NoContent() : DocumentNotFound();

    private DocumentActor CurrentActor() => new(
        Guid.TryParse(userManager.GetUserId(User), out var id) ? id : null,
        userManager.GetUserName(User) ?? "Unknown",
        User.IsInRole(RoleNames.Admin));

    private NotFoundObjectResult DocumentNotFound() => NotFound(new { message = "Document not found." });
}
