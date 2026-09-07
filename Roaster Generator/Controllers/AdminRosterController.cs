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
    RosterTimerService rosterTimer,
    RosterPlanService rosterPlans) : ApiControllerBase
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

    [HttpPost("timer")]
    public async Task<IActionResult> StartTimer(
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
            return Accepted(rosterTimer.Start(request.WeekOffset));
        }
        catch (RosterTimerAlreadyRunningException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    [HttpPost("timer/cancel")]
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
            var errors = validationResult.Errors
                .GroupBy(error => error.PropertyName)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(error => error.ErrorMessage).ToArray());

            return ValidationError(errors, "The selected week is invalid.");
        }

        return rosterTimer.Cancel(selection.WeekOffset, request.JobId)
            ? Accepted(new { message = "WebSocket timer cancellation requested." })
            : NotFound(new { message = "No active WebSocket timer was found for the selected week." });
    }
}
