namespace Roaster_Generator.Security;

public static class RoleNames
{
    public const string Admin = "Admin";

    public const string LegacyUser = "User";

    public const string Driver = "Driver";

    public const string InStore = "InStore";

    public const string Manager = "Manager";

    public static string[] Normalize(IEnumerable<string>? roles) =>
        (roles ?? [])
            .Select(Normalize)
            .Where(role => role is not null)
            .Select(role => role!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    public static bool IsEmployeeRole(string? role) =>
        string.Equals(role, Driver, StringComparison.Ordinal) ||
        string.Equals(role, InStore, StringComparison.Ordinal) ||
        string.Equals(role, Manager, StringComparison.Ordinal);

    private static string? Normalize(string? role) => role?.ToLowerInvariant() switch
    {
        "admin" => Admin,
        "driver" => Driver,
        "instore" => InStore,
        "manager" => Manager,
        _ => null
    };
}
