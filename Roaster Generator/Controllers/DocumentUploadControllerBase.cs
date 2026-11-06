using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Roaster_Generator.Contracts.Documents;
using Roaster_Generator.Entities;
using Roaster_Generator.Security;
using Roaster_Generator.Services;

namespace Roaster_Generator.Controllers;

/// <summary>The analyse, submit and discard steps shared by employee and administrator uploads.</summary>
public abstract class DocumentUploadControllerBase(
    DocumentDraftService drafts,
    UserManager<ApplicationUser> userManager,
    IValidator<DocumentAnalyseForm> uploadValidator,
    IValidator<DocumentSubmitRequest> submitValidator) : ApiControllerBase
{
    /// <summary>Allows for multipart overhead around the largest accepted upload.</summary>
    protected const long MaxRequestSize = DocumentFileValidator.MaxTotalSize + 1024 * 1024;

    protected UserManager<ApplicationUser> Users { get; } = userManager;

    protected DocumentActor CurrentActor() => new(
        Guid.TryParse(Users.GetUserId(User), out var id) ? id : null,
        Users.GetUserName(User) ?? "Unknown",
        User.IsInRole(RoleNames.Admin));

    protected async Task<IActionResult> AnalyseCoreAsync(
        Employee employee, DocumentAnalyseForm form, CancellationToken ct)
    {
        var validationError = await ValidateRequestAsync(uploadValidator, form, "The upload is invalid.", ct);
        if (validationError is not null) return validationError;

        try
        {
            var files = await DocumentFileValidator.ReadAsync(form.Files, ct);
            return Ok(await drafts.AnalyseAsync(employee, CurrentActor(), files, form.DocumentTypeId, ct));
        }
        catch (InvalidDataException exception)
        {
            return ValidationError(new Dictionary<string, string[]> { ["files"] = [exception.Message] }, "The upload is invalid.");
        }
        catch (DocumentUnavailableException exception)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = exception.Message });
        }
    }

    protected async Task<IActionResult> SubmitCoreAsync(
        Employee employee, Guid draftId, DocumentSubmitRequest request, CancellationToken ct)
    {
        var validationError = await ValidateRequestAsync(submitValidator, request, "The document is invalid.", ct);
        if (validationError is not null) return validationError;

        try
        {
            var saved = await drafts.SubmitAsync(employee, CurrentActor(), draftId, request, ct);
            return saved is null ? DraftNotFound() : Ok(saved);
        }
        catch (DocumentSettingsException exception)
        {
            return ValidationError(new Dictionary<string, string[]> { ["documentTypeId"] = [exception.Message] }, "The document is invalid.");
        }
        catch (DbUpdateException)
        {
            return Conflict(new { message = "This document was just updated. Reload and try again." });
        }
    }

    protected async Task<IActionResult> DiscardCoreAsync(Employee employee, Guid draftId, CancellationToken ct) =>
        await drafts.DiscardAsync(employee, CurrentActor(), draftId, ct) ? NoContent() : DraftNotFound();

    protected async Task<IActionResult> StartVehicleChangeCoreAsync(
        Employee employee,
        VehicleChangeRequest request,
        DocumentVehicleService vehicles,
        IValidator<VehicleChangeRequest> validator,
        CancellationToken ct)
    {
        var validationError = await ValidateRequestAsync(validator, request, "The registration is invalid.", ct);
        if (validationError is not null) return validationError;

        try
        {
            await vehicles.StartChangeAsync(employee, CurrentActor(), request.Registration, ct);
            return NoContent();
        }
        catch (DocumentSettingsException exception)
        {
            return ValidationError(new Dictionary<string, string[]> { ["registration"] = [exception.Message] }, "The registration is invalid.");
        }
        catch (DbUpdateException)
        {
            return Conflict(new { message = "This car change was just updated. Reload and try again." });
        }
    }

    protected async Task<IActionResult> CancelVehicleChangeCoreAsync(
        Employee employee, DocumentVehicleService vehicles, CancellationToken ct) =>
        await vehicles.CancelChangeAsync(employee, CurrentActor(), ct)
            ? NoContent()
            : NotFound(new { message = "No car change is open." });

    private NotFoundObjectResult DraftNotFound() =>
        NotFound(new { message = "This upload has expired. Choose the file again." });
}
