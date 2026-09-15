using Roaster_Generator.Entities;
using Roaster_Generator.Security;

namespace Roaster_Generator.Services;

public static class RosterKinds
{
    public const string Drivers = "drivers";
    public const string Inside = "inside";
    public const string InvalidMessage = "Roster type must be drivers or inside.";
    public const string GenerationDisabledMessage = "Only driver rosters can be generated automatically. Inside rosters are created manually.";

    public static bool IsValid(string? kind) => kind is Drivers or Inside;
    public static bool IsEnabled(string? kind) => IsValid(kind);
    public static bool IsGenerationEnabled(string? kind) => kind == Drivers;

    public static void EnsureEnabled(string? kind)
    {
        if (!IsEnabled(kind)) throw new RosterInputException(InvalidMessage);
    }

    public static void EnsureGenerationEnabled(string? kind)
    {
        if (!IsGenerationEnabled(kind)) throw new RosterInputException(GenerationDisabledMessage);
    }

    public static string[] Roles(Employee employee) =>
        (employee.DriverProfile is not null ? new[] { RoleNames.Driver } : [])
        .Concat(employee.InStoreProfile is not null ? [RoleNames.InStore] : [])
        .Concat(employee.ManagerProfile is not null ? [RoleNames.Manager] : []).ToArray();
}
