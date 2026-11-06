using System.Globalization;
using Roaster_Generator.Entities;

namespace Roaster_Generator.Services;

/// <summary>
/// Names downloads by the onboarding convention, such as "Anna Passport Ireland 31.12.30.pdf".
/// Fields a document doesn't have are left out, and reference numbers never go in a file name.
/// </summary>
public static class DocumentFileNames
{
    private const string DateFormat = "dd.MM.yy";

    public static string Build(
        string givenName, EmployeeDocument document, string extension, int pageIndex, int pageCount)
    {
        var type = document.DocumentType;
        var parts = new List<string?> { givenName.Trim() };
        parts.AddRange(type.Code switch
        {
            DocumentTypeCodes.Passport => ["Passport", CountryName(document.IssuingCountry), Date(document)],
            DocumentTypeCodes.DrivingLicence => ["License", Date(document)],
            DocumentTypeCodes.Irp => ["IRP", document.Stamp, Date(document)],
            DocumentTypeCodes.LabourMarketPermission => ["LMP", Date(document)],
            DocumentTypeCodes.TemporaryResidenceCertificate => ["TRC", Date(document)],
            DocumentTypeCodes.Spf => ["SPF"],
            DocumentTypeCodes.FoodHandler => ["Food handler"],
            DocumentTypeCodes.PenaltyPoints => ["PP", Date(document)],
            DocumentTypeCodes.Obu => ["OBU", document.VehicleRegistration],
            DocumentTypeCodes.Nct => ["NCT", document.VehicleRegistration, Date(document)],
            DocumentTypeCodes.MotorTax => ["Tax", document.VehicleRegistration, Date(document)],
            DocumentTypeCodes.Insurance => ["Insurance", document.VehicleRegistration, Date(document)],
            _ => [type.Name, document.VehicleRegistration, Date(document)]
        });
        parts.Add(PageLabel(pageIndex, pageCount));

        var name = string.Join(' ', parts.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part!.Trim()));
        return Clean(name) + extension;
    }

    private static string? Date(EmployeeDocument document) =>
        document.ExpiryDate?.ToString(DateFormat, CultureInfo.InvariantCulture);

    private static string? PageLabel(int pageIndex, int pageCount) => pageCount switch
    {
        <= 1 => null,
        2 => pageIndex == 0 ? "front" : "back",
        _ => $"page {pageIndex + 1}"
    };

    private static string? CountryName(string? code)
    {
        if (string.IsNullOrEmpty(code)) return null;
        try
        {
            return new RegionInfo(code).EnglishName;
        }
        catch (ArgumentException)
        {
            return code;
        }
    }

    /// <summary>Names and registrations come from documents, so path characters are removed.</summary>
    private static string Clean(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var cleaned = new string(name.Select(character => invalid.Contains(character) || char.IsControl(character) ? ' ' : character).ToArray());
        return string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
