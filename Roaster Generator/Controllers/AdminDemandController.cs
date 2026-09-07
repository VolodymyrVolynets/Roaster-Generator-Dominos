using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Roaster_Generator.Contracts.Demand;
using Roaster_Generator.Security;
using Roaster_Generator.Services;

namespace Roaster_Generator.Controllers;

[ApiController]
[Authorize(Roles = RoleNames.Admin)]
[Route("api/admin/demand")]
public sealed class AdminDemandController(DemandService demand) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetPlans(CancellationToken cancellationToken) =>
        Ok(await demand.GetPlansAsync(cancellationToken));

    [HttpGet("{planId:guid}")]
    public async Task<IActionResult> GetPlan(
        Guid planId,
        CancellationToken cancellationToken)
    {
        var plan = await demand.GetPlanAsync(planId, cancellationToken);
        return plan is null
            ? NotFound(new { message = "Demand plan not found." })
            : Ok(plan);
    }

    [HttpPost("paste")]
    public async Task<IActionResult> ImportPaste(
        [FromBody] DemandImportRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await demand.ImportTextAsync(request, cancellationToken));
        }
        catch (DemandValidationException exception)
        {
            return DemandValidationError(exception);
        }
    }

    [HttpPost("upload")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> ImportExcel(
        [FromForm(Name = "file")] IFormFile? file,
        [FromForm] string? name,
        [FromForm] string? weekStart,
        CancellationToken cancellationToken)
    {
        try
        {
            if (file is null || file.Length == 0)
            {
                throw new DemandValidationException("Select an Excel file before importing it.");
            }

            if (!DateOnly.TryParse(
                    weekStart,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsedWeekStart))
            {
                throw new DemandValidationException("A valid Monday week start date is required.");
            }

            await using var stream = file.OpenReadStream();
            return Ok(await demand.ImportExcelAsync(
                name,
                parsedWeekStart,
                stream,
                cancellationToken));
        }
        catch (DemandValidationException exception)
        {
            return DemandValidationError(exception);
        }
    }

    [HttpPut("{planId:guid}")]
    public async Task<IActionResult> Update(
        Guid planId,
        [FromBody] DemandPlanUpdateRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await demand.UpdateAsync(planId, request, cancellationToken));
        }
        catch (DemandValidationException exception)
        {
            return DemandValidationError(exception);
        }
    }

    [HttpDelete("{planId:guid}")]
    public async Task<IActionResult> Delete(
        Guid planId,
        CancellationToken cancellationToken)
    {
        try
        {
            await demand.DeleteAsync(planId, cancellationToken);
            return NoContent();
        }
        catch (DemandValidationException exception)
        {
            return DemandValidationError(exception);
        }
    }

    private IActionResult DemandValidationError(DemandValidationException exception) =>
        ValidationError(
            new Dictionary<string, string[]>
            {
                ["demand"] = [exception.Message]
            },
            "The demand plan is invalid.");
}
