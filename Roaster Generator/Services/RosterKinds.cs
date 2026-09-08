using Roaster_Generator.Entities;
using Roaster_Generator.Security;

namespace Roaster_Generator.Services;

public static class RosterKinds
{
    public const string Drivers = "drivers";
    public const string Inside = "inside";
    public const string DisabledMessage = "Only driver rosters are available. In-store and manager rosters are disabled.";

    // Legacy inside snapshots remain readable internally; application entry points
    // accept only the currently enabled driver roster.
    public static bool IsValid(string? kind) => kind is Drivers or Inside;
    public static bool IsEnabled(string? kind) => kind == Drivers;

    public static void EnsureEnabled(string? kind)
    {
        if (!IsEnabled(kind)) throw new RosterInputException(DisabledMessage);
    }

    public static int TargetHours(Employee employee, string kind) => kind == Inside
        ? employee.ManagerProfile?.TargetHours ?? employee.InStoreProfile?.TargetHours ?? 0
        : employee.DriverProfile?.TargetHours ?? 0;

    public static string[] Roles(Employee employee) =>
        (employee.DriverProfile is not null ? new[] { RoleNames.Driver } : [])
        .Concat(employee.InStoreProfile is not null ? [RoleNames.InStore] : [])
        .Concat(employee.ManagerProfile is not null ? [RoleNames.Manager] : []).ToArray();
}
