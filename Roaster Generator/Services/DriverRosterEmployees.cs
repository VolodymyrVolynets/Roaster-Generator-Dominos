using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Security;

namespace Roaster_Generator.Services;

internal static class DriverRosterEmployees
{
    // Both current Identity roles and profiles are required. A stale profile must
    // never grant roster access, and inside employees are not scheduled as drivers.
    public static IQueryable<Employee> Query(AppDbContext db)
    {
        var memberships = db.UserRoles
            .Join(db.Roles, membership => membership.RoleId, role => role.Id,
                (membership, role) => new { membership.UserId, role.Name })
            .Join(db.Users.Where(user => user.EmployeeId.HasValue), membership => membership.UserId,
                user => user.Id, (membership, user) => new { EmployeeId = user.EmployeeId!.Value, Role = membership.Name });
        var driverIds = memberships.Where(membership => membership.Role == RoleNames.Driver)
            .Select(membership => membership.EmployeeId);
        var insideIds = memberships.Where(membership => membership.Role == RoleNames.Manager || membership.Role == RoleNames.InStore)
            .Select(membership => membership.EmployeeId);

        return db.Employees.Where(employee => employee.IsActive && employee.DriverProfile != null
            && employee.ManagerProfile == null && employee.InStoreProfile == null
            && driverIds.Contains(employee.Id) && !insideIds.Contains(employee.Id));
    }
}

internal static class InsideRosterEmployees
{
    // Inside eligibility also requires the current Identity role and matching
    // profile. Managers take precedence when an employee has both inside roles.
    public static IQueryable<Employee> Query(AppDbContext db)
    {
        var memberships = db.UserRoles
            .Join(db.Roles, membership => membership.RoleId, role => role.Id,
                (membership, role) => new { membership.UserId, role.Name })
            .Join(db.Users.Where(user => user.EmployeeId.HasValue), membership => membership.UserId,
                user => user.Id, (membership, user) => new { EmployeeId = user.EmployeeId!.Value, Role = membership.Name });
        var managerIds = memberships.Where(membership => membership.Role == RoleNames.Manager)
            .Select(membership => membership.EmployeeId);
        var inStoreIds = memberships.Where(membership => membership.Role == RoleNames.InStore)
            .Select(membership => membership.EmployeeId);

        return db.Employees.Where(employee => employee.IsActive &&
            (employee.ManagerProfile != null && managerIds.Contains(employee.Id) ||
             employee.InStoreProfile != null && inStoreIds.Contains(employee.Id)));
    }
}
