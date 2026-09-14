namespace Roaster_Generator.Enums;

public static class DemandKinds
{
    public const string Outside = "outside";
    public const string Inside = "inside";

    public static bool IsValid(string? kind) => kind is Outside or Inside;
}
