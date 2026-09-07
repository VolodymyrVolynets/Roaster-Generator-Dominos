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
    RosterGenerationService rosterGeneration) : ApiControllerBase
{
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
