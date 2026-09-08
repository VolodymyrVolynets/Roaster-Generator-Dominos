namespace Roaster_Generator.Configuration;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public string AdminUsername { get; set; } = string.Empty;

    public string AdminPassword { get; set; } = string.Empty;

    // One-time recovery switches. Keep these disabled during normal operation.
    public bool ResetAdminPasswordOnStartup { get; set; }

    public bool ResetEmployeePasswordsOnStartup { get; set; }

    public const string DefaultEmployeePassword = "12345";
}
