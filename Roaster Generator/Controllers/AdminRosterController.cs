using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Roaster_Generator.Contracts.Roster;
using Roaster_Generator.Contracts.Schedules;
using Roaster_Generator.Security;
using Roaster_Generator.Services;

namespace Roaster_Generator.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.Manager)]
[Route("api/admin/roster")]
public sealed class AdminRosterController(
    IValidator<WeekSelectionRequest> weekValidator,
    IValidator<RosterPlanUpdateRequest> planUpdateValidator,
    IValidator<RosterSettingsRequest> settingsValidator,
    RosterTimerService rosterTimer,
    RosterPlanService rosterPlans,
    RosterSettingsService settings) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] int? weekOffset,
        [FromQuery] DateOnly? weekStart,
        CancellationToken cancellationToken)
    {
        if (weekStart.HasValue)
        {
            if (weekStart.Value.DayOfWeek != DayOfWeek.Monday)
                return BadRequest(new { message = "Select the Monday of a saved roster week." });
            var saved = await rosterPlans.GetAsync(weekStart.Value, cancellationToken);
            return saved is null ? NotFound(new { message = "A roster has not been generated for this week." }) : Ok(saved);
        }
        var selection = new WeekSelectionRequest
        {
            WeekOffset = weekOffset ?? WeeklyScheduleService.MinWeekOffset
        };
        var validationResult = await weekValidator.ValidateAsync(selection, cancellationToken);

        if (!validationResult.IsValid)
        {
            return ValidationError(ToErrors(validationResult), "The selected week is invalid.");
        }

        var plan = await rosterPlans.GetAsync(selection.WeekOffset, cancellationToken);
        return plan is null
            ? NotFound(new { message = "A roster has not been generated for this week." })
            : Ok(plan);
    }

    [HttpPut]
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    public async Task<IActionResult> Update(
        [FromBody] RosterPlanUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var validationResult = await ValidateRequestAsync(
            planUpdateValidator,
            request,
            "The edited roster is invalid.",
            cancellationToken);

        if (validationResult is not null)
        {
            return validationResult;
        }

        try
        {
            return Ok(await rosterPlans.UpdateAsync(request, cancellationToken));
        }
        catch (RosterInputException exception)
        {
            var messages = new[] { exception.Message }
                .Concat(exception.Diagnostics)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return ValidationError(
                new Dictionary<string, string[]> { ["roster"] = messages },
                "The edited roster is invalid.");
        }
    }

    [HttpGet("summary")]
    [Authorize(Policy = AuthorizationPolicies.Admin)]
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
            return ValidationError(ToErrors(validationResult), "The selected week is invalid.");
        }

        return Ok(await rosterPlans.GetSummaryAsync(selection.WeekOffset, cancellationToken));
    }

    [HttpPost("generate")]
    [HttpPost("timer")]
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    public async Task<IActionResult> StartTimer(
        [FromBody] WeekSelectionRequest request,
        CancellationToken cancellationToken)
    {
        var validationResult = await weekValidator.ValidateAsync(request, cancellationToken);

        if (!validationResult.IsValid)
        {
            return ValidationError(ToErrors(validationResult), "The selected week is invalid.");
        }

        try
        {
            return Accepted(rosterTimer.Start(request.WeekOffset));
        }
        catch (RosterTimerAlreadyRunningException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    [HttpPost("cancel")]
    [HttpPost("timer/cancel")]
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    public async Task<IActionResult> Cancel(
        [FromBody] RosterTimerCancelRequest request,
        CancellationToken cancellationToken)
    {
        var selection = new WeekSelectionRequest
        {
            WeekOffset = request.WeekOffset
        };
        var validationResult = await weekValidator.ValidateAsync(selection, cancellationToken);

        if (!validationResult.IsValid)
        {
            return ValidationError(ToErrors(validationResult), "The selected week is invalid.");
        }

        return rosterTimer.Cancel(selection.WeekOffset, request.JobId)
            ? Accepted(new { message = "Roster generation cancellation requested." })
            : NotFound(new { message = "No active roster generation was found for the selected week." });
    }

    [HttpGet("settings")]
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    public async Task<IActionResult> GetSettings(CancellationToken ct) => Ok(await settings.GetAsync(ct));

    [HttpPut("settings")]
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    public async Task<IActionResult> SaveSettings([FromBody] RosterSettingsRequest request, CancellationToken ct)
    {
        var validationResult = await ValidateRequestAsync(
            settingsValidator,
            request,
            "The roster settings are invalid.",
            ct);

        return validationResult ?? Ok(await settings.SaveAsync(request, ct));
    }

    [HttpGet("history")]
    public async Task<IActionResult> History(CancellationToken ct) => Ok(await rosterPlans.GetHistoryAsync(ct));

    [HttpGet("jobs")]
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    public async Task<IActionResult> Jobs([FromQuery] int weekOffset, CancellationToken ct)
    {
        var validation = await weekValidator.ValidateAsync(new WeekSelectionRequest { WeekOffset = weekOffset }, ct);
        if (!validation.IsValid) return BadRequest(new { message = "Select a week from next week through three weeks ahead." });
        return Ok(rosterTimer.GetLogs(WeeklyScheduleService.GetWeekMonday(weekOffset)));
    }
}
