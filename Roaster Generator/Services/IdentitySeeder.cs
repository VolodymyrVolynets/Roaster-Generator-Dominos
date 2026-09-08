using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Roaster_Generator.Configuration;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Enums;
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
        await EnsureRoleAsync(roleManager, RoleNames.Driver);
        await EnsureRoleAsync(roleManager, RoleNames.InStore);
        await EnsureRoleAsync(roleManager, RoleNames.Manager);
        await EnsureAdminAsync(userManager, authOptions);

        var employees = await db.Employees
            .OrderBy(employee => employee.EmployeeNumber)
            .ToListAsync(cancellationToken);

        foreach (var employee in employees)
        {
            await EnsureEmployeeUserAsync(
                userManager,
                db,
                employee,
                authOptions,
                cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
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

        if (await userManager.IsInRoleAsync(admin, RoleNames.LegacyUser))
        {
            var removeResult = await userManager.RemoveFromRoleAsync(admin, RoleNames.LegacyUser);
            EnsureSucceeded(removeResult, "removing the legacy User role from the admin");
        }
    }

    private static async Task EnsureEmployeeUserAsync(
        UserManager<ApplicationUser> userManager,
        AppDbContext db,
        Employee employee,
        AuthOptions authOptions,
        CancellationToken cancellationToken)
    {
        var user = await userManager.Users
            .SingleOrDefaultAsync(item => item.EmployeeId == employee.Id, cancellationToken);

        var userWasCreated = false;

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
            userWasCreated = true;
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

        if (authOptions.ResetEmployeePasswordsOnStartup && !userWasCreated)
        {
            await ResetPasswordAsync(
                userManager,
                user,
                AuthOptions.DefaultEmployeePassword,
                $"resetting the password for employee {employee.EmployeeNumber}");
        }

        var roles = (await userManager.GetRolesAsync(user)).ToArray();
        var employeeRoles = roles
            .Where(RoleNames.IsEmployeeRole)
            .ToArray();

        if (employeeRoles.Length == 0)
        {
            var roleResult = await userManager.AddToRoleAsync(user, RoleNames.Driver);
            EnsureSucceeded(roleResult, $"assigning the Driver role to employee {employee.EmployeeNumber}");
            employeeRoles = [RoleNames.Driver];
        }

        if (roles.Contains(RoleNames.LegacyUser, StringComparer.Ordinal))
        {
            var removeResult = await userManager.RemoveFromRoleAsync(user, RoleNames.LegacyUser);
            EnsureSucceeded(removeResult, $"removing the legacy User role from employee {employee.EmployeeNumber}");
        }

        await EnsureProfilesAsync(db, employee, employeeRoles, cancellationToken);
    }

    private static async Task EnsureProfilesAsync(
        AppDbContext db,
        Employee employee,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken)
    {
        if (roles.Contains(RoleNames.Driver, StringComparer.Ordinal) &&
            !await db.DriverProfiles.AnyAsync(profile => profile.EmployeeId == employee.Id, cancellationToken))
        {
            db.DriverProfiles.Add(new DriverProfile
            {
                EmployeeId = employee.Id,
                TargetHours = 20,
                CanWorkAlone = true,
                DriverType = DriverType.Car
            });
        }

        if (roles.Contains(RoleNames.InStore, StringComparer.Ordinal) &&
            !await db.InStoreProfiles.AnyAsync(profile => profile.EmployeeId == employee.Id, cancellationToken))
        {
            db.InStoreProfiles.Add(new InStoreProfile { EmployeeId = employee.Id });
        }

        if (roles.Contains(RoleNames.Manager, StringComparer.Ordinal) &&
            !await db.ManagerProfiles.AnyAsync(profile => profile.EmployeeId == employee.Id, cancellationToken))
        {
            db.ManagerProfiles.Add(new ManagerProfile { EmployeeId = employee.Id });
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

    private static async Task ResetPasswordAsync(
        UserManager<ApplicationUser> userManager,
        ApplicationUser user,
        string password,
        string operation)
    {
        if (await userManager.HasPasswordAsync(user))
        {
            var removeResult = await userManager.RemovePasswordAsync(user);
            EnsureSucceeded(removeResult, operation);
        }

        var addResult = await userManager.AddPasswordAsync(user, password);
        EnsureSucceeded(addResult, operation);
    }
}
