using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Roaster_Generator.Configuration;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Security;

namespace Roaster_Generator.Services;

public static class IdentitySeeder
{
    public static async Task SeedAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var db = services.GetRequiredService<AppDbContext>();
        var authOptions = services.GetRequiredService<IOptions<AuthOptions>>().Value;

        if (string.IsNullOrWhiteSpace(authOptions.AdminUsername) ||
            string.IsNullOrWhiteSpace(authOptions.AdminPassword))
        {
            throw new InvalidOperationException(
                "Auth:AdminUsername and Auth:AdminPassword must be configured through environment variables.");
        }

        await EnsureRoleAsync(roleManager, RoleNames.Admin);
        await EnsureRoleAsync(roleManager, RoleNames.User);
        await EnsureAdminAsync(userManager, authOptions);

        var employees = await db.Employees
            .OrderBy(employee => employee.EmployeeNumber)
            .ToListAsync(cancellationToken);

        foreach (var employee in employees)
        {
            await EnsureEmployeeUserAsync(userManager, employee);
        }
    }

    private static async Task EnsureRoleAsync(
        RoleManager<IdentityRole<Guid>> roleManager,
        string roleName)
    {
        if (await roleManager.RoleExistsAsync(roleName))
        {
            return;
        }

        var result = await roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
        EnsureSucceeded(result, $"creating role '{roleName}'");
    }

    private static async Task EnsureAdminAsync(
        UserManager<ApplicationUser> userManager,
        AuthOptions authOptions)
    {
        var admin = await userManager.FindByNameAsync(authOptions.AdminUsername);

        if (admin is null)
        {
            admin = new ApplicationUser
            {
                UserName = authOptions.AdminUsername,
                Email = $"{authOptions.AdminUsername}@local.invalid",
                EmailConfirmed = true
            };

            var createResult = await userManager.CreateAsync(admin, authOptions.AdminPassword);
            EnsureSucceeded(createResult, "creating the admin user");
        }

        if (!await userManager.IsInRoleAsync(admin, RoleNames.Admin))
        {
            var roleResult = await userManager.AddToRoleAsync(admin, RoleNames.Admin);
            EnsureSucceeded(roleResult, "assigning the Admin role");
        }
    }

    private static async Task EnsureEmployeeUserAsync(
        UserManager<ApplicationUser> userManager,
        Employee employee)
    {
        var user = await userManager.Users
            .SingleOrDefaultAsync(item => item.EmployeeId == employee.Id);

        if (user is null)
        {
            user = await userManager.FindByNameAsync(employee.EmployeeNumber);
        }

        if (user is not null &&
            user.EmployeeId is null &&
            await userManager.IsInRoleAsync(user, RoleNames.Admin))
        {
            throw new InvalidOperationException(
                $"The employee number '{employee.EmployeeNumber}' conflicts with the administrator username.");
        }

        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = employee.EmployeeNumber,
                Email = $"employee-{employee.EmployeeNumber}@local.invalid",
                EmailConfirmed = true,
                EmployeeId = employee.Id
            };

            var createResult = await userManager.CreateAsync(
                user,
                AuthOptions.DefaultEmployeePassword);
            EnsureSucceeded(createResult, $"creating user for employee {employee.EmployeeNumber}");
        }
        else if (user.EmployeeId is null)
        {
            user.EmployeeId = employee.Id;
            var updateResult = await userManager.UpdateAsync(user);
            EnsureSucceeded(updateResult, $"linking user to employee {employee.EmployeeNumber}");
        }
        else if (user.EmployeeId != employee.Id)
        {
            throw new InvalidOperationException(
                $"The employee number '{employee.EmployeeNumber}' is already linked to another employee.");
        }

        if (!await userManager.IsInRoleAsync(user, RoleNames.User))
        {
            var roleResult = await userManager.AddToRoleAsync(user, RoleNames.User);
            EnsureSucceeded(roleResult, $"assigning the User role to employee {employee.EmployeeNumber}");
        }
    }

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (result.Succeeded)
        {
            return;
        }

        var errors = string.Join(", ", result.Errors.Select(error => error.Description));
        throw new InvalidOperationException($"Failed while {operation}: {errors}");
    }
}
