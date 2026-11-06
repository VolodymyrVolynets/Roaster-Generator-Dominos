using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Roaster_Generator.Configuration;
using Roaster_Generator.Contracts.Documents;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Security;
using Roaster_Generator.Services;

namespace Roaster_Generator.Controllers;

/// <summary>An employee's own documents. There is deliberately no route that returns file contents.</summary>
[ApiController]
[Authorize]
[Route("api/documents")]
public sealed class DocumentsController(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    DocumentChecklistService checklist,
    DocumentDraftService drafts,
    IValidator<DocumentAnalyseForm> uploadValidator,
    IValidator<DocumentSubmitRequest> submitValidator)
    : DocumentUploadControllerBase(drafts, userManager, uploadValidator, submitValidator)
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var employee = await GetCurrentEmployeeAsync(ct);
        return employee is null ? Forbid() : Ok(await checklist.GetAsync(employee.Id, ct));
    }

    [HttpPost("analyses")]
    [RequestSizeLimit(MaxRequestSize)]
    [EnableRateLimiting(DocumentRateLimits.AnalysisPolicy)]
    public async Task<IActionResult> Analyse([FromForm] DocumentAnalyseForm form, CancellationToken ct)
    {
        var employee = await GetCurrentEmployeeAsync(ct);
        return employee is null ? Forbid() : await AnalyseCoreAsync(employee, form, ct);
    }

    [HttpPost("analyses/{draftId:guid}/submit")]
    public async Task<IActionResult> Submit(Guid draftId, [FromBody] DocumentSubmitRequest request, CancellationToken ct)
    {
        var employee = await GetCurrentEmployeeAsync(ct);
        return employee is null ? Forbid() : await SubmitCoreAsync(employee, draftId, request, ct);
    }

    [HttpDelete("analyses/{draftId:guid}")]
    public async Task<IActionResult> Discard(Guid draftId, CancellationToken ct)
    {
        var employee = await GetCurrentEmployeeAsync(ct);
        return employee is null ? Forbid() : await DiscardCoreAsync(employee, draftId, ct);
    }

    [HttpPost("vehicle-change")]
    public async Task<IActionResult> StartVehicleChange(
        [FromBody] VehicleChangeRequest request,
        [FromServices] DocumentVehicleService vehicles,
        [FromServices] IValidator<VehicleChangeRequest> validator,
        CancellationToken ct)
    {
        var employee = await GetCurrentEmployeeAsync(ct);
        return employee is null ? Forbid() : await StartVehicleChangeCoreAsync(employee, request, vehicles, validator, ct);
    }

    [HttpDelete("vehicle-change")]
    public async Task<IActionResult> CancelVehicleChange([FromServices] DocumentVehicleService vehicles, CancellationToken ct)
    {
        var employee = await GetCurrentEmployeeAsync(ct);
        return employee is null ? Forbid() : await CancelVehicleChangeCoreAsync(employee, vehicles, ct);
    }

    private async Task<Employee?> GetCurrentEmployeeAsync(CancellationToken ct)
    {
        var user = await Users.GetUserAsync(User);
        if (user?.EmployeeId is not Guid employeeId) return null;
        var roles = await Users.GetRolesAsync(user);
        if (!roles.Any(RoleNames.IsEmployeeRole)) return null;
        return await db.Employees.SingleOrDefaultAsync(
            employee => employee.Id == employeeId && employee.IsActive && !employee.IsDeleted, ct);
    }
}
