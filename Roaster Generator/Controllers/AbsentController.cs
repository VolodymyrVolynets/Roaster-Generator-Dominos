using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Roaster_Generator.Contracts.Absent;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Security;
using Roaster_Generator.Services;

namespace Roaster_Generator.Controllers;

[ApiController]
[Authorize(Roles = RoleNames.Driver)]
[Route("api/absent")]
public sealed class AbsentController(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    AbsentFormService forms,
    IValidator<AbsentFormCreateRequest> validator,
    ApplicationEventPublisher? events = null) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var employee = await GetCurrentDriverAsync(ct);
        if (employee is null) return Forbid();
        return Ok(await forms.GetForDriverAsync(employee, ct));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AbsentFormCreateRequest request, CancellationToken ct)
    {
        var employee = await GetCurrentDriverAsync(ct);
        if (employee is null) return Forbid();
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return ValidationError(ToErrors(validation), "The absent form is invalid.");

        var form = await forms.SubmitAsync(employee, request, ct);
        if (form is null)
            return ValidationError(new Dictionary<string, string[]>
            {
                ["savedRosterShiftId"] = ["Select an available shift from your saved driver roster, or enter the shift manually. The roster may have changed."]
            }, "The absent form is invalid.");

        if (events is not null) await events.AbsentChangedAsync(form.Id, employee.Id, ct);
        return Ok(form);
    }

    private async Task<Employee?> GetCurrentDriverAsync(CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(User);
        if (user?.EmployeeId is not Guid employeeId || !await userManager.IsInRoleAsync(user, RoleNames.Driver))
            return null;
        return await db.Employees.AsNoTracking().SingleOrDefaultAsync(
            employee => employee.Id == employeeId && employee.IsActive, ct);
    }
}
