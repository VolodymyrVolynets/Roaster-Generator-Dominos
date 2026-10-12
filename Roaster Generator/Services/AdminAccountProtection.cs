using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Security;

namespace Roaster_Generator.Services;

public sealed class AdminAccountProtection(AppDbContext db)
{
    // All employee mutations that can reduce administrator access take the same
    // transaction-scoped lock before reading roles or counting usable admins.
    public async Task LockAsync(CancellationToken ct)
    {
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(724190631)", ct);
    }

    public Task<bool> IsAdminAsync(ApplicationUser user, CancellationToken ct) =>
        (from membership in db.UserRoles
         join role in db.Roles on membership.RoleId equals role.Id
         where membership.UserId == user.Id && role.Name == RoleNames.Admin
         select membership).AnyAsync(ct);

    public async Task<string?> RemovalErrorAsync(ApplicationUser user, ClaimsPrincipal caller, CancellationToken ct)
    {
        if (caller.FindFirstValue(ClaimTypes.NameIdentifier) == user.Id.ToString())
            return "You cannot remove or deactivate your own administrator account or remove your Admin role.";

        var anotherActiveAdmin = await (
            from membership in db.UserRoles
            join role in db.Roles on membership.RoleId equals role.Id
            join account in db.Users on membership.UserId equals account.Id
            where role.Name == RoleNames.Admin && account.Id != user.Id &&
                  (!account.LockoutEnabled || account.LockoutEnd == null || account.LockoutEnd <= DateTimeOffset.UtcNow) &&
                  (account.EmployeeId == null || db.Employees.Any(employee =>
                      employee.Id == account.EmployeeId && employee.IsActive))
            select account.Id).AnyAsync(ct);

        return anotherActiveAdmin ? null : "The last active administrator cannot be removed, deactivated or demoted.";
    }

    public async Task DisableBootstrapAdminsAsync(CancellationToken ct)
    {
        var hasEmployeeAdmin = await (
            from membership in db.UserRoles
            join role in db.Roles on membership.RoleId equals role.Id
            join account in db.Users on membership.UserId equals account.Id
            where role.Name == RoleNames.Admin && account.EmployeeId != null &&
                  (!account.LockoutEnabled || account.LockoutEnd == null || account.LockoutEnd <= DateTimeOffset.UtcNow) &&
                  db.Employees.Any(employee => employee.Id == account.EmployeeId && employee.IsActive)
            select account.Id).AnyAsync(ct);
        if (!hasEmployeeAdmin) return;

        var bootstrapAdmins = await (
            from account in db.Users
            join membership in db.UserRoles on account.Id equals membership.UserId
            join role in db.Roles on membership.RoleId equals role.Id
            where account.EmployeeId == null && role.Name == RoleNames.Admin
            select account).ToListAsync(ct);
        foreach (var account in bootstrapAdmins)
        {
            account.LockoutEnabled = true;
            account.LockoutEnd = DateTimeOffset.MaxValue;
        }
        await db.SaveChangesAsync(ct);
    }
}
