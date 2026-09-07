using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Roaster_Generator.Contracts.Roster;
using Roaster_Generator.Contracts.Schedules;
using Roaster_Generator.Security;
using Roaster_Generator.Services;

namespace Roaster_Generator.Controllers;

[ApiController]
[Authorize(Roles = RoleNames.Admin)]
[Route("api/admin/roster")]
public sealed class AdminRosterController(
    IValidator<WeekSelectionRequest> weekValidator,
    RosterGenerationService rosterGeneration,
    RosterPlanService rosterPlans,
    RosterGenerationSettingsService rosterSettings) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] int? weekOffset,
        CancellationToken cancellationToken)
    {
        var selection = new WeekSelectionRequest
        {
            WeekOffset = weekOffset ?? WeeklyScheduleService.MinWeekOffset
        };
        var validationResult = await weekValidator.ValidateAsync(selection, cancellationToken);

        if (!validationResult.IsValid)
        {
            var errors = validationResult.Errors
                .GroupBy(error => error.PropertyName)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(error => error.ErrorMessage).ToArray());

            return ValidationError(errors, "The selected week is invalid.");
        }

        var plan = await rosterPlans.GetAsync(selection.WeekOffset, cancellationToken);
        return plan is null
            ? NotFound(new { message = "A roster has not been generated for this week." })
            : Ok(plan);
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(
        [FromQuery] int? weekOffset,
        CancellationToken cancellationToken)
    {
        var selection = new WeekSelectionRequest
        {
            WeekOffset = weekOffset ?? WeeklyScheduleService.MinWeekOffset
        };
        var validationResult = await weekValidator.ValidateAsync(selection, cancellationToken);

        if (!validationResult.IsValid)
        {
            var errors = validationResult.Errors
                .GroupBy(error => error.PropertyName)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(error => error.ErrorMessage).ToArray());

            return ValidationError(errors, "The selected week is invalid.");
        }

        return Ok(await rosterPlans.GetSummaryAsync(selection.WeekOffset, cancellationToken));
    }

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings(CancellationToken cancellationToken) =>
        Ok(await rosterSettings.GetAsync(cancellationToken));

    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings(
        [FromBody] RosterGenerationSettingsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await rosterSettings.UpdateAsync(request, cancellationToken));
        }
        catch (RosterGenerationSettingsValidationException exception)
        {
            return ValidationError(
                new Dictionary<string, string[]> { ["settings"] = [exception.Message] },
                "Generator settings are invalid.");
        }
    }

    [HttpPost("generate")]
    public async Task<IActionResult> Generate(
        [FromBody] WeekSelectionRequest request,
        CancellationToken cancellationToken)
    {
        var validationResult = await weekValidator.ValidateAsync(request, cancellationToken);

        if (!validationResult.IsValid)
        {
            var errors = validationResult.Errors
                .GroupBy(error => error.PropertyName)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(error => error.ErrorMessage).ToArray());

            return ValidationError(errors, "The selected week is invalid.");
        }

        try
        {
            return Accepted(rosterGeneration.Start(request.WeekOffset));
        }
        catch (RosterGenerationAlreadyRunningException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }
}
