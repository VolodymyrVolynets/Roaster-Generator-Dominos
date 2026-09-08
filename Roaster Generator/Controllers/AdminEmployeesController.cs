using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Roaster_Generator.Configuration;
using Roaster_Generator.Contracts.Employees;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Security;
using Roaster_Generator.Services;

namespace Roaster_Generator.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.Manager)]
[Route("api/admin/employees")]
public sealed class AdminEmployeesController(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    IValidator<EmployeeRequest> employeeValidator) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetEmployees(CancellationToken cancellationToken)
    {
        var employees = await db.Employees
            .AsNoTracking()
            .Include(employee => employee.DriverProfile)
            .Include(employee => employee.InStoreProfile)
            .Include(employee => employee.ManagerProfile)
            .OrderBy(employee => employee.LastName)
            .ThenBy(employee => employee.FirstName)
            .ToListAsync(cancellationToken);

        var responses = new List<EmployeeResponse>(employees.Count);

        foreach (var employee in employees)
        {
            responses.Add(await ToEmployeeResponseAsync(employee, cancellationToken));
        }

        return Ok(responses);
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] EmployeeRequest request,
        CancellationToken cancellationToken)
    {
        var validationResult = await ValidateRequestAsync(
            employeeValidator,
            request,
            "The employee is invalid.",
            cancellationToken);

        if (validationResult is not null)
        {
            return validationResult;
        }

        var roles = RoleNames.Normalize(request.Roles);

        if (roles.Contains(RoleNames.Admin, StringComparer.Ordinal) && !User.IsInRole(RoleNames.Admin))
        {
            return Forbid();
        }

        var employeeNumber = request.EmployeeNumber.Trim();
        var firstName = request.FirstName.Trim();
        var lastName = request.LastName.Trim();
        var phoneNumber = request.PhoneNumber.Trim();
        var payrollNumber = NormalizeOptional(request.PayrollNumber);

        if (await db.Employees.AnyAsync(employee => employee.EmployeeNumber == employeeNumber, cancellationToken) ||
            await userManager.FindByNameAsync(employeeNumber) is not null)
        {
            return ValidationError(
                new Dictionary<string, string[]>
                {
                    ["employeeNumber"] = ["Employee number is already in use."]
                },
                "The employee is invalid.");
        }

        if (payrollNumber is not null &&
            await db.Employees.AnyAsync(employee => employee.PayrollNumber == payrollNumber, cancellationToken))
        {
            return ValidationError(
                new Dictionary<string, string[]>
                {
                    ["payrollNumber"] = ["Payroll number is already in use."]
                },
                "The employee is invalid.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var employee = new Employee
        {
            EmployeeNumber = employeeNumber,
            FirstName = firstName,
            LastName = lastName,
            PhoneNumber = phoneNumber,
            PayrollNumber = payrollNumber,
            HourlyRate = request.HourlyRate ?? 14.5m,
            IsActive = true
        };

        db.Employees.Add(employee);
        await db.SaveChangesAsync(cancellationToken);

        SynchronizeProfiles(employee, roles, request);
        await db.SaveChangesAsync(cancellationToken);

        var user = new ApplicationUser
        {
            UserName = employeeNumber,
            Email = $"employee-{employeeNumber}@local.invalid",
            EmailConfirmed = true,
            EmployeeId = employee.Id
        };
        var userResult = await userManager.CreateAsync(user, AuthOptions.DefaultEmployeePassword);

        if (!userResult.Succeeded)
        {
            return IdentityValidationError(userResult, "The employee user could not be created.");
        }

        var roleResult = await userManager.AddToRolesAsync(user, roles);

        if (!roleResult.Succeeded)
        {
            return IdentityValidationError(roleResult, "The employee roles could not be assigned.");
        }

        await transaction.CommitAsync(cancellationToken);
        return Ok(await ToEmployeeResponseAsync(employee, cancellationToken));
    }

    [HttpPut("{employeeId:guid}")]
    public async Task<IActionResult> Update(
        Guid employeeId,
        [FromBody] EmployeeRequest request,
        CancellationToken cancellationToken)
    {
        var employee = await db.Employees
            .Include(item => item.DriverProfile)
            .Include(item => item.InStoreProfile)
            .Include(item => item.ManagerProfile)
            .SingleOrDefaultAsync(item => item.Id == employeeId, cancellationToken);

        if (employee is null)
        {
            return NotFound(new { message = "Employee not found." });
        }

        var validationResult = await ValidateRequestAsync(
            employeeValidator,
            request,
            "The employee is invalid.",
            cancellationToken);

        if (validationResult is not null)
        {
            return validationResult;
        }

        var roles = RoleNames.Normalize(request.Roles);
        var employeeNumber = request.EmployeeNumber.Trim();
        var firstName = request.FirstName.Trim();
        var lastName = request.LastName.Trim();
        var phoneNumber = request.PhoneNumber.Trim();
        var payrollNumber = NormalizeOptional(request.PayrollNumber);

        if (await db.Employees.AnyAsync(
                item => item.Id != employeeId && item.EmployeeNumber == employeeNumber,
                cancellationToken))
        {
            return ValidationError(
                new Dictionary<string, string[]>
                {
                    ["employeeNumber"] = ["Employee number is already in use."]
                },
                "The employee is invalid.");
        }

        if (payrollNumber is not null &&
            await db.Employees.AnyAsync(
                item => item.Id != employeeId && item.PayrollNumber == payrollNumber,
                cancellationToken))
        {
            return ValidationError(
                new Dictionary<string, string[]>
                {
                    ["payrollNumber"] = ["Payroll number is already in use."]
                },
                "The employee is invalid.");
        }

        var user = await userManager.Users
            .SingleOrDefaultAsync(item => item.EmployeeId == employeeId, cancellationToken);
        var currentRoles = user is null
            ? []
            : (await userManager.GetRolesAsync(user)).ToArray();

        if ((roles.Contains(RoleNames.Admin, StringComparer.Ordinal) ||
             currentRoles.Contains(RoleNames.Admin, StringComparer.Ordinal)) &&
            !User.IsInRole(RoleNames.Admin))
        {
            return Forbid();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        if (user is not null && !string.Equals(user.UserName, employeeNumber, StringComparison.Ordinal))
        {
            var userResult = await userManager.SetUserNameAsync(user, employeeNumber);

            if (!userResult.Succeeded)
            {
                return IdentityValidationError(userResult, "The employee login could not be updated.");
            }
        }

        employee.EmployeeNumber = employeeNumber;
        employee.FirstName = firstName;
        employee.LastName = lastName;
        employee.PhoneNumber = phoneNumber;
        employee.PayrollNumber = payrollNumber;
        employee.HourlyRate = request.HourlyRate ?? employee.HourlyRate;

        SynchronizeProfiles(employee, roles, request);

        if (user is not null)
        {
            var rolesToRemove = currentRoles
                .Where(role => IsManagedRole(role) && !roles.Contains(role, StringComparer.Ordinal))
                .ToArray();
            var rolesToAdd = roles
                .Where(role => !currentRoles.Contains(role, StringComparer.Ordinal))
                .ToArray();

            if (rolesToRemove.Length > 0)
            {
                var removeResult = await userManager.RemoveFromRolesAsync(user, rolesToRemove);

                if (!removeResult.Succeeded)
                {
                    return IdentityValidationError(removeResult, "The employee roles could not be updated.");
                }
            }

            if (rolesToAdd.Length > 0)
            {
                var addResult = await userManager.AddToRolesAsync(user, rolesToAdd);

                if (!addResult.Succeeded)
                {
                    return IdentityValidationError(addResult, "The employee roles could not be updated.");
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Ok(await ToEmployeeResponseAsync(employee, cancellationToken));
    }

    [HttpDelete("{employeeId:guid}")]
    public async Task<IActionResult> Delete(
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var employee = await db.Employees.SingleOrDefaultAsync(
            item => item.Id == employeeId,
            cancellationToken);

        if (employee is null)
        {
            return NotFound(new { message = "Employee not found." });
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var user = await userManager.Users
            .SingleOrDefaultAsync(item => item.EmployeeId == employeeId, cancellationToken);

        if (user is not null)
        {
            var userResult = await userManager.DeleteAsync(user);

            if (!userResult.Succeeded)
            {
                return IdentityValidationError(userResult, "The employee login could not be deleted.");
            }
        }

        db.Employees.Remove(employee);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return NoContent();
    }

    [HttpPost("{employeeId:guid}/deactivate")]
    public Task<IActionResult> Deactivate(Guid employeeId, CancellationToken cancellationToken) =>
        SetStatusAsync(employeeId, false, cancellationToken);

    [HttpPost("{employeeId:guid}/reactivate")]
    public Task<IActionResult> Reactivate(Guid employeeId, CancellationToken cancellationToken) =>
        SetStatusAsync(employeeId, true, cancellationToken);

    private async Task<IActionResult> SetStatusAsync(
        Guid employeeId,
        bool isActive,
        CancellationToken cancellationToken)
    {
        var employee = await db.Employees
            .Include(item => item.DriverProfile)
            .Include(item => item.InStoreProfile)
            .Include(item => item.ManagerProfile)
            .SingleOrDefaultAsync(item => item.Id == employeeId, cancellationToken);

        if (employee is null)
        {
            return NotFound(new { message = "Employee not found." });
        }

        employee.IsActive = isActive;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(await ToEmployeeResponseAsync(employee, cancellationToken));
    }

    private async Task<EmployeeResponse> ToEmployeeResponseAsync(
        Employee employee,
        CancellationToken cancellationToken)
    {
        var user = await userManager.Users
            .SingleOrDefaultAsync(item => item.EmployeeId == employee.Id, cancellationToken);
        var roles = user is null
            ? []
            : await userManager.GetRolesAsync(user);

        return EmployeeResponseMapper.ToResponse(employee, roles);
    }

    private void SynchronizeProfiles(
        Employee employee,
        IReadOnlyCollection<string> roles,
        EmployeeRequest request)
    {
        if (roles.Contains(RoleNames.Driver, StringComparer.Ordinal))
        {
            employee.DriverProfile ??= new DriverProfile { EmployeeId = employee.Id };
            employee.DriverProfile.TargetHours = request.TargetHours;
            employee.DriverProfile.DriverType = request.DriverType;
        }
        else if (employee.DriverProfile is not null)
        {
            db.DriverProfiles.Remove(employee.DriverProfile);
            employee.DriverProfile = null;
        }

        if (roles.Contains(RoleNames.InStore, StringComparer.Ordinal))
        {
            employee.InStoreProfile ??= new InStoreProfile { EmployeeId = employee.Id };
            employee.InStoreProfile.TargetHours = request.InsideTargetHours;
        }
        else if (employee.InStoreProfile is not null)
        {
            db.InStoreProfiles.Remove(employee.InStoreProfile);
            employee.InStoreProfile = null;
        }

        if (roles.Contains(RoleNames.Manager, StringComparer.Ordinal))
        {
            employee.ManagerProfile ??= new ManagerProfile { EmployeeId = employee.Id };
            employee.ManagerProfile.TargetHours = request.InsideTargetHours;
        }
        else if (employee.ManagerProfile is not null)
        {
            db.ManagerProfiles.Remove(employee.ManagerProfile);
            employee.ManagerProfile = null;
        }
    }

    private static bool IsManagedRole(string role) =>
        role is RoleNames.Admin or RoleNames.Driver or RoleNames.InStore or RoleNames.Manager or RoleNames.LegacyUser;

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private IActionResult IdentityValidationError(IdentityResult result, string title) =>
        ValidationError(
            result.Errors
                .GroupBy(error => "identity")
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(error => error.Description).ToArray()),
            title);
}
