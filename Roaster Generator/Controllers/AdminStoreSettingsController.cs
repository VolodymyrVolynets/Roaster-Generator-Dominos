using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Roaster_Generator.Contracts.Settings;
using Roaster_Generator.Security;
using Roaster_Generator.Services;

namespace Roaster_Generator.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.Admin)]
[Route("api/admin/settings/store")]
public sealed class AdminStoreSettingsController(
    StoreSettingsService settings,
    IValidator<StoreSettingsRequest> validator) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) => Ok(await settings.GetAsync(ct));

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] StoreSettingsRequest request, CancellationToken ct)
    {
        var validationError = await ValidateRequestAsync(
            validator, request, "The store settings are invalid.", ct);
        return validationError ?? Ok(await settings.SaveAsync(request, ct));
    }
}
