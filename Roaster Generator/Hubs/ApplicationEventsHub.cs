using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Security;

namespace Roaster_Generator.Hubs;

public static class ApplicationEventGroups
{
    public const string Management = "management";

    public static string Employee(Guid employeeId) => $"employee:{employeeId:N}";

    public static string Role(string role) => $"role:{role}";
}

[Authorize]
public sealed class ApplicationEventsHub(
    UserManager<ApplicationUser> userManager,
    AppDbContext db) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var user = await userManager.GetUserAsync(Context.User!);
        if (user is null)
        {
            Context.Abort();
            return;
        }

        if (user.EmployeeId is Guid employeeId)
        {
            var active = await db.Employees.AsNoTracking()
                .AnyAsync(employee => employee.Id == employeeId && employee.IsActive);
            if (!active)
            {
                Context.Abort();
                return;
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, ApplicationEventGroups.Employee(employeeId));
        }

        var roles = await userManager.GetRolesAsync(user);
        foreach (var role in roles.Where(role =>
                     role == RoleNames.Admin || role == RoleNames.Manager || RoleNames.IsEmployeeRole(role)))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, ApplicationEventGroups.Role(role));
        }

        if (roles.Contains(RoleNames.Admin) || roles.Contains(RoleNames.Manager))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, ApplicationEventGroups.Management);
        }

        await base.OnConnectedAsync();
    }
}
