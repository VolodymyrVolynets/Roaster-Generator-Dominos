using System.Collections.Frozen;

namespace Roaster_Generator.Services;

/// <summary>Country groups from the required-document rules, keyed by ISO 3166-1 alpha-2 code.</summary>
public static class DocumentCountries
{
    private static readonly FrozenSet<string> EuMembers = new[]
    {
        "AT", "BE", "BG", "HR", "CY", "CZ", "DK", "EE", "FI", "FR", "DE", "GR", "HU", "IE",
        "IT", "LV", "LT", "LU", "MT", "NL", "PL", "PT", "RO", "SK", "SI", "ES", "SE"
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> ImmigrationExempt = EuMembers
        .Concat(["NO", "IS", "LI", "CH", "GB"])
        .ToFrozenSet(StringComparer.Ordinal);

    public static string? Normalize(string? country)
    {
        var value = country?.Trim().ToUpperInvariant();
        return value is { Length: 2 } && value.All(char.IsAsciiLetterUpper) ? value : null;
    }

    /// <summary>EU, EEA, Switzerland and the UK: passports that need no immigration permission.</summary>
    public static bool IsImmigrationExempt(string country) => ImmigrationExempt.Contains(country);

    public static bool IsEuMember(string country) => EuMembers.Contains(country);
}
