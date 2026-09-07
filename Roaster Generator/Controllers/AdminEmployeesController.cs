using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Roaster_Generator.Configuration;
using Roaster_Generator.Contracts.Employees;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Security;

namespace Roaster_Generator.Controllers;

[ApiController]
[Authorize(Roles = RoleNames.Admin)]
[Route("api/admin/employees")]
public sealed class AdminEmployeesController(
    AppDbContext db,
    UserManager<ApplicationUser> userManager) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetEmployees(CancellationToken cancellationToken)
    {
        var employees = (await db.Employees
                .AsNoTracking()
                .OrderBy(employee => employee.LastName)
                .ThenBy(employee => employee.FirstName)
                .ToListAsync(cancellationToken))
            .Select(ToEmployeeResponse)
            .ToList();

        return Ok(employees);
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] EmployeeRequest request,
        CancellationToken cancellationToken)
    {
        var employeeNumber = request.EmployeeNumber.Trim();
        var firstName = request.FirstName.Trim();
        var lastName = request.LastName.Trim();
        var phoneNumber = request.PhoneNumber.Trim();

        var validation = ValidateEmployee(employeeNumber, firstName, lastName, request.TargetHours);

        if (validation is not null)
        {
            return validation;
        }

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

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var employee = new Employee
        {
            EmployeeNumber = employeeNumber,
            FirstName = firstName,
            LastName = lastName,
            PhoneNumber = phoneNumber,
            TargetHours = request.TargetHours,
            CanWorkAlone = request.CanWorkAlone,
            IsActive = true
        };

        db.Employees.Add(employee);
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
            return ValidationError(
                userResult.Errors
                    .GroupBy(error => "user")
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(error => error.Description).ToArray()),
                "The employee user could not be created.");
        }

        var roleResult = await userManager.AddToRoleAsync(user, RoleNames.User);

        if (!roleResult.Succeeded)
        {
            return ValidationError(
                roleResult.Errors
                    .GroupBy(error => "role")
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(error => error.Description).ToArray()),
                "The employee role could not be assigned.");
        }

        await transaction.CommitAsync(cancellationToken);
        return Ok(ToEmployeeResponse(employee));
    }

    [HttpPut("{employeeId:guid}")]
    public async Task<IActionResult> Update(
        Guid employeeId,
        [FromBody] EmployeeRequest request,
        CancellationToken cancellationToken)
    {
        var employee = await db.Employees.SingleOrDefaultAsync(
            item => item.Id == employeeId,
            cancellationToken);

        if (employee is null)
        {
            return NotFound(new { message = "Employee not found." });
        }

        var employeeNumber = request.EmployeeNumber.Trim();
        var firstName = request.FirstName.Trim();
        var lastName = request.LastName.Trim();
        var phoneNumber = request.PhoneNumber.Trim();

        var validation = ValidateEmployee(employeeNumber, firstName, lastName, request.TargetHours);

        if (validation is not null)
        {
            return validation;
        }

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

        var user = await userManager.Users
            .SingleOrDefaultAsync(item => item.EmployeeId == employeeId, cancellationToken);

        if (user is not null && !string.Equals(user.UserName, employeeNumber, StringComparison.Ordinal))
        {
            var userResult = await userManager.SetUserNameAsync(user, employeeNumber);

            if (!userResult.Succeeded)
            {
                return ValidationError(
                    userResult.Errors
                        .GroupBy(error => "user")
                        .ToDictionary(
                            group => group.Key,
                            group => group.Select(error => error.Description).ToArray()),
                    "The employee login could not be updated.");
            }
        }

        employee.EmployeeNumber = employeeNumber;
        employee.FirstName = firstName;
        employee.LastName = lastName;
        employee.PhoneNumber = phoneNumber;
        employee.TargetHours = request.TargetHours;
        employee.CanWorkAlone = request.CanWorkAlone;
        await db.SaveChangesAsync(cancellationToken);

        return Ok(ToEmployeeResponse(employee));
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
                return ValidationError(
                    userResult.Errors
                        .GroupBy(error => "user")
                        .ToDictionary(
                            group => group.Key,
                            group => group.Select(error => error.Description).ToArray()),
                    "The employee login could not be deleted.");
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
        var employee = await db.Employees.SingleOrDefaultAsync(
            item => item.Id == employeeId,
            cancellationToken);

        if (employee is null)
        {
            return NotFound(new { message = "Employee not found." });
        }

        employee.IsActive = isActive;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToEmployeeResponse(employee));
    }

    private static IActionResult? ValidateEmployee(
        string employeeNumber,
        string firstName,
        string lastName,
        int targetHours)
    {
        if (string.IsNullOrWhiteSpace(employeeNumber) ||
            string.IsNullOrWhiteSpace(firstName) ||
            string.IsNullOrWhiteSpace(lastName)
            )
        {
            return new BadRequestObjectResult(new ValidationProblemDetails(
                new Dictionary<string, string[]>
                {
                    ["employee"] = ["Employee number, first name, and last name are required."]
                })
                {
                    Title = "The employee is invalid."
                });
        }

        return targetHours is < 3 or > 168
            ? new BadRequestObjectResult(new ValidationProblemDetails(
                new Dictionary<string, string[]>
                {
                    ["targetHours"] = ["Target hours must be between 3 and 168 per week."]
                })
                {
                    Title = "The employee is invalid."
                })
            : null;
    }

    private static EmployeeResponse ToEmployeeResponse(Employee employee) => new()
    {
        Id = employee.Id,
        EmployeeNumber = employee.EmployeeNumber,
        FirstName = employee.FirstName,
        LastName = employee.LastName,
        PhoneNumber = employee.PhoneNumber,
        IsActive = employee.IsActive,
        TargetHours = employee.TargetHours,
        CanWorkAlone = employee.CanWorkAlone
    };
}
