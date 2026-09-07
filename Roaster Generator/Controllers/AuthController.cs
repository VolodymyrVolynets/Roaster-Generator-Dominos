using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Roaster_Generator.Contracts.Auth;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Security;

namespace Roaster_Generator.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    AppDbContext db) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByNameAsync(request.Username);

        if (user is null)
        {
            return Unauthorized();
        }

        if (user.EmployeeId is Guid employeeId &&
            !await db.Employees.AnyAsync(
                employee => employee.Id == employeeId && employee.IsActive,
                cancellationToken))
        {
            return Unauthorized();
        }

        var passwordResult = await signInManager.CheckPasswordSignInAsync(
            user,
            request.Password,
            lockoutOnFailure: false);

        if (!passwordResult.Succeeded)
        {
            return Unauthorized();
        }

        await signInManager.SignInAsync(user, isPersistent: true);
        return Ok();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);

        if (user is null)
        {
            return Unauthorized();
        }

        Employee? employee = null;

        if (user.EmployeeId is Guid employeeId)
        {
            employee = await db.Employees
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == employeeId, cancellationToken);

            if (employee is null || !employee.IsActive)
            {
                return Unauthorized();
            }
        }

        var roles = await userManager.GetRolesAsync(user);

        return Ok(new CurrentUserResponse
        {
            Username = user.UserName ?? string.Empty,
            IsAdmin = roles.Contains(RoleNames.Admin),
            EmployeeId = user.EmployeeId,
            EmployeeName = employee is null
                ? null
                : $"{employee.FirstName} {employee.LastName}".Trim()
        });
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return NoContent();
    }
}
