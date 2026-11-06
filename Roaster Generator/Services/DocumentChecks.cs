using System.Globalization;
using System.Text;
using Roaster_Generator.Enums;

namespace Roaster_Generator.Services;

public enum DocumentWarningCode
{
    Expired,
    MissingExpiry,
    NameMismatch,
    LicenceNotEuIssued,
    RegistrationMismatch
}

public sealed record DocumentWarning(DocumentWarningCode Code, string Message);

public sealed record DocumentFacts(
    string DocumentTypeCode,
    string DocumentTypeName,
    DocumentExpiryRule ExpiryRule,
    string? HolderName,
    string? IssuingCountry,
    DateOnly? PrintedExpiryDate,
    DateOnly? SigningDate,
    int? PenaltyPoints,
    string? VehicleRegistration);

/// <summary>
/// Deterministic checks on the fields of a document. These decide the warnings, so the
/// AI only reads the document and never judges it.
/// </summary>
public static class DocumentChecks
{
    private const int MaxPointsForLongInterval = 3;

    private static readonly HashSet<string> VehicleDocumentCodes = new(StringComparer.Ordinal)
    {
        DocumentTypeCodes.Nct, DocumentTypeCodes.MotorTax, DocumentTypeCodes.Insurance, DocumentTypeCodes.Obu
    };

    public static bool IsVehicleDocument(string documentTypeCode) => VehicleDocumentCodes.Contains(documentTypeCode);

    public static DateOnly? ResolveExpiry(
        DocumentExpiryRule rule, DateOnly? printedExpiry, DateOnly? signingDate, int? penaltyPoints) => rule switch
    {
        DocumentExpiryRule.Printed => printedExpiry,
        DocumentExpiryRule.PenaltyPoints => PenaltyPointsDueDate(signingDate, penaltyPoints),
        _ => null
    };

    /// <summary>Signing date plus 3 calendar months for 0 to 3 points, or 1 for more than 3.</summary>
    public static DateOnly? PenaltyPointsDueDate(DateOnly? signingDate, int? penaltyPoints)
    {
        if (signingDate is not DateOnly signed || penaltyPoints is not int points || points < 0) return null;
        return signed.AddMonths(points <= MaxPointsForLongInterval ? 3 : 1);
    }

    /// <summary>A longer name on the document is accepted; every name on the employee record must appear on it.</summary>
    public static bool NameMatches(string holderName, string firstName, string lastName)
    {
        var holder = NameTokens(holderName).ToHashSet(StringComparer.Ordinal);
        var employee = NameTokens($"{firstName} {lastName}").ToArray();
        return employee.Length > 0 && employee.All(holder.Contains);
    }

    public static string NormalizeRegistration(string registration) =>
        string.Concat(registration.Where(char.IsLetterOrDigit)).ToUpperInvariant();

    public static IReadOnlyList<DocumentWarning> Evaluate(
        DocumentFacts facts,
        string employeeFirstName,
        string employeeLastName,
        IEnumerable<string> registrationsOnFile,
        DateOnly today)
    {
        var warnings = new List<DocumentWarning>();
        var expiry = ResolveExpiry(facts.ExpiryRule, facts.PrintedExpiryDate, facts.SigningDate, facts.PenaltyPoints);

        if (facts.ExpiryRule != DocumentExpiryRule.None && expiry is null)
            warnings.Add(new(DocumentWarningCode.MissingExpiry, $"The {facts.DocumentTypeName} expiry date could not be determined."));
        if (expiry < today)
            warnings.Add(new(DocumentWarningCode.Expired, $"This {facts.DocumentTypeName} has expired."));

        if (!string.IsNullOrWhiteSpace(facts.HolderName) &&
            !NameMatches(facts.HolderName, employeeFirstName, employeeLastName))
            warnings.Add(new(DocumentWarningCode.NameMismatch, "The name on the document does not match your profile."));

        if (facts.DocumentTypeCode == DocumentTypeCodes.DrivingLicence &&
            DocumentCountries.Normalize(facts.IssuingCountry) is { } country && !DocumentCountries.IsEuMember(country))
            warnings.Add(new(DocumentWarningCode.LicenceNotEuIssued, "The driving licence is not EU-issued."));

        if (IsVehicleDocument(facts.DocumentTypeCode) && !string.IsNullOrWhiteSpace(facts.VehicleRegistration))
        {
            var registration = NormalizeRegistration(facts.VehicleRegistration);
            if (registrationsOnFile.Any(other => NormalizeRegistration(other) != registration))
                warnings.Add(new(DocumentWarningCode.RegistrationMismatch,
                    "The vehicle registration does not match the current vehicle."));
        }

        return warnings;
    }

    private static IEnumerable<string> NameTokens(string name)
    {
        var separated = new StringBuilder(name.Length);
        foreach (var character in name.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            if (character is '\'' or '’') continue;
            separated.Append(char.IsLetterOrDigit(character) ? char.ToUpperInvariant(character) : ' ');
        }

        return separated.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
}
