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

/// <summary>Uploads on an employee's behalf. Administrators confirm what they upload, so these are not flagged for review.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.Admin)]
[Route("api/admin/employees/{employeeId:guid}/documents")]
public sealed class AdminEmployeeDocumentsController(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    DocumentDraftService drafts,
    IValidator<DocumentAnalyseForm> uploadValidator,
    IValidator<DocumentSubmitRequest> submitValidator)
    : DocumentUploadControllerBase(drafts, userManager, uploadValidator, submitValidator)
{
    [HttpPost("analyses")]
    [RequestSizeLimit(MaxRequestSize)]
    [EnableRateLimiting(DocumentRateLimits.AnalysisPolicy)]
    public async Task<IActionResult> Analyse(Guid employeeId, [FromForm] DocumentAnalyseForm form, CancellationToken ct)
    {
        var employee = await FindEmployeeAsync(employeeId, ct);
        return employee is null ? EmployeeNotFound() : await AnalyseCoreAsync(employee, form, ct);
    }

    [HttpPost("analyses/{draftId:guid}/submit")]
    public async Task<IActionResult> Submit(
        Guid employeeId, Guid draftId, [FromBody] DocumentSubmitRequest request, CancellationToken ct)
    {
        var employee = await FindEmployeeAsync(employeeId, ct);
        return employee is null ? EmployeeNotFound() : await SubmitCoreAsync(employee, draftId, request, ct);
    }

    [HttpDelete("analyses/{draftId:guid}")]
    public async Task<IActionResult> Discard(Guid employeeId, Guid draftId, CancellationToken ct)
    {
        var employee = await FindEmployeeAsync(employeeId, ct);
        return employee is null ? EmployeeNotFound() : await DiscardCoreAsync(employee, draftId, ct);
    }

    [HttpPost("vehicle-change")]
    public async Task<IActionResult> StartVehicleChange(
        Guid employeeId,
        [FromBody] VehicleChangeRequest request,
        [FromServices] DocumentVehicleService vehicles,
        [FromServices] IValidator<VehicleChangeRequest> validator,
        CancellationToken ct)
    {
        var employee = await FindEmployeeAsync(employeeId, ct);
        return employee is null ? EmployeeNotFound() : await StartVehicleChangeCoreAsync(employee, request, vehicles, validator, ct);
    }

    [HttpDelete("vehicle-change")]
    public async Task<IActionResult> CancelVehicleChange(
        Guid employeeId, [FromServices] DocumentVehicleService vehicles, CancellationToken ct)
    {
        var employee = await FindEmployeeAsync(employeeId, ct);
        return employee is null ? EmployeeNotFound() : await CancelVehicleChangeCoreAsync(employee, vehicles, ct);
    }

    private Task<Employee?> FindEmployeeAsync(Guid employeeId, CancellationToken ct) =>
        db.Employees.SingleOrDefaultAsync(employee => employee.Id == employeeId && !employee.IsDeleted, ct);

    private NotFoundObjectResult EmployeeNotFound() => NotFound(new { message = "Employee not found." });
}
