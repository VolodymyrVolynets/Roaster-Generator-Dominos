using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Roaster_Generator.Contracts.Documents;
using Roaster_Generator.Enums;
using Roaster_Generator.Security;
using Roaster_Generator.Services;

namespace Roaster_Generator.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.Admin)]
[Route("api/admin")]
public sealed class AdminDocumentSettingsController(
    DocumentSettingsService settings,
    IValidator<DocumentTypeRequest> typeValidator,
    IValidator<DocumentRequirementListRequest> listValidator) : ApiControllerBase
{
    private const string InvalidTypeTitle = "The document type is invalid.";
    private const string InvalidListTitle = "The required document list is invalid.";

    [HttpGet("document-types")]
    public async Task<IActionResult> GetTypes(CancellationToken ct) => Ok(await settings.GetTypesAsync(ct));

    [HttpPost("document-types")]
    public async Task<IActionResult> CreateType([FromBody] DocumentTypeRequest request, CancellationToken ct)
    {
        var validationError = await ValidateRequestAsync(typeValidator, request, InvalidTypeTitle, ct);
        if (validationError is not null) return validationError;
        return await HandleAsync(async () => Ok(await settings.CreateTypeAsync(request, ct)), InvalidTypeTitle);
    }

    [HttpPut("document-types/{id:guid}")]
    public async Task<IActionResult> UpdateType(Guid id, [FromBody] DocumentTypeRequest request, CancellationToken ct)
    {
        var validationError = await ValidateRequestAsync(typeValidator, request, InvalidTypeTitle, ct);
        if (validationError is not null) return validationError;
        return await HandleAsync(async () =>
            await settings.UpdateTypeAsync(id, request, ct) is { } updated ? Ok(updated) : TypeNotFound(), InvalidTypeTitle);
    }

    [HttpDelete("document-types/{id:guid}")]
    public Task<IActionResult> DeleteType(Guid id, CancellationToken ct) => HandleAsync(
        async () => await settings.DeleteTypeAsync(id, ct) ? NoContent() : TypeNotFound(), "The document type cannot be deleted.");

    [HttpGet("document-requirements")]
    public async Task<IActionResult> GetLists(CancellationToken ct) => Ok(await settings.GetListsAsync(ct));

    [HttpPut("document-requirements/{category}")]
    public async Task<IActionResult> SaveList(
        DocumentCategory category, [FromBody] DocumentRequirementListRequest request, CancellationToken ct)
    {
        if (!Enum.IsDefined(category)) return NotFound(new { message = "Employee type not found." });
        var validationError = await ValidateRequestAsync(listValidator, request, InvalidListTitle, ct);
        if (validationError is not null) return validationError;
        return await HandleAsync(async () => Ok(await settings.SaveListAsync(category, request, ct)), InvalidListTitle);
    }

    [HttpGet("document-types/{id:guid}/template")]
    public async Task<IActionResult> GetTemplate(
        Guid id, [FromServices] DocumentSigningService signing, CancellationToken ct) =>
        await signing.GetTemplateAsync(id, ct) is { } template ? Ok(template) : TemplateNotFound();

    [HttpPost("document-types/{id:guid}/template")]
    [RequestSizeLimit(DocumentFileValidator.MaxTotalSize + 1024 * 1024)]
    public async Task<IActionResult> UploadTemplate(
        Guid id, IFormFile? file, [FromServices] DocumentSigningService signing, CancellationToken ct)
    {
        const string title = "The form template is invalid.";
        if (file is null || file.Length is <= 0 or > DocumentFileValidator.MaxTotalSize)
            return ValidationError(new Dictionary<string, string[]> { ["file"] = ["Choose a PDF of 20 MB or smaller."] }, title);

        try
        {
            using var content = new MemoryStream((int)file.Length);
            await file.CopyToAsync(content, ct);
            return await signing.SaveTemplateAsync(id, content.ToArray(), Path.GetFileName(file.FileName), ct) is { } template
                ? Ok(template)
                : TypeNotFound();
        }
        catch (InvalidDataException exception)
        {
            return ValidationError(new Dictionary<string, string[]> { ["file"] = [exception.Message] }, title);
        }
    }

    [HttpPut("document-types/{id:guid}/template/fields")]
    public async Task<IActionResult> SaveTemplateFields(
        Guid id,
        [FromBody] DocumentTemplateFieldsRequest request,
        [FromServices] DocumentSigningService signing,
        [FromServices] IValidator<DocumentTemplateFieldsRequest> validator,
        CancellationToken ct)
    {
        const string title = "The form fields are invalid.";
        var validationError = await ValidateRequestAsync(validator, request, title, ct);
        if (validationError is not null) return validationError;
        return await HandleAsync(async () =>
            await signing.SaveFieldsAsync(id, request.Fields, ct) is { } template ? Ok(template) : TemplateNotFound(), title);
    }

    private NotFoundObjectResult TemplateNotFound() => NotFound(new { message = "No form has been uploaded for this document type." });

    private NotFoundObjectResult TypeNotFound() => NotFound(new { message = "Document type not found." });

    private async Task<IActionResult> HandleAsync(Func<Task<IActionResult>> action, string title)
    {
        try
        {
            return await action();
        }
        catch (DocumentSettingsException exception)
        {
            return ValidationError(new Dictionary<string, string[]> { [string.Empty] = [exception.Message] }, title);
        }
    }
}
