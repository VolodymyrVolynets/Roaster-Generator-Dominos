using System.Globalization;
using System.Text.Json;
using Roaster_Generator.Contracts.Documents;

namespace Roaster_Generator.Services;

public sealed record DocumentCatalogueEntry(string Code, string Name);

public sealed class DocumentAnalysis
{
    public const string UnknownType = "unknown";

    public string DocumentType { get; set; } = UnknownType;
    public bool Readable { get; set; }
    public bool BothSides { get; set; }
    public string? Summary { get; set; }
    public List<string> Problems { get; set; } = [];
    public DocumentFields Fields { get; set; } = new();
}

public interface IDocumentAnalyser
{
    bool IsConfigured { get; }

    /// <returns>The analysis, or null when the files could not be analysed. Only cancellation throws.</returns>
    Task<DocumentAnalysis?> AnalyseAsync(
        IReadOnlyList<ValidatedDocumentFile> files,
        IReadOnlyList<DocumentCatalogueEntry> catalogue,
        CancellationToken cancellationToken);

    /// <summary>Reads a form an employee has just signed, with their typed answers beside it.</summary>
    /// <returns>The problems found, empty when there are none, or null when the form could not be checked. Only cancellation throws.</returns>
    Task<IReadOnlyList<string>?> CheckSignedFormAsync(
        byte[] pdf,
        string formName,
        string employeeName,
        string employeeRole,
        IReadOnlyList<string> answers,
        CancellationToken cancellationToken);
}

/// <summary>
/// Turns model output into trusted values. The output comes from a document anyone can write,
/// so it is checked and limited here even though the request constrains its shape.
/// </summary>
public static class DocumentAnalysisParser
{
    private const int MaxProblems = 5;
    private const int MaxProblemLength = 200;
    private const int MaxSummaryLength = 300;

    // Values validated as a whole must not be shortened first: "Ireland" cut to two letters reads as Iran.
    private const int MaxUncheckedLength = 50;

    public static DocumentAnalysis? Parse(string json, IEnumerable<string> knownCodes)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var type = Text(root, "documentType", 40);
            return new DocumentAnalysis
            {
                DocumentType = type is not null && knownCodes.Contains(type, StringComparer.Ordinal)
                    ? type
                    : DocumentAnalysis.UnknownType,
                Readable = root.TryGetProperty("readable", out var readable) && readable.ValueKind == JsonValueKind.True,
                BothSides = root.TryGetProperty("bothSides", out var bothSides) && bothSides.ValueKind == JsonValueKind.True,
                Summary = Text(root, "summary", MaxSummaryLength),
                Problems = Problems(root),
                Fields = new DocumentFields
                {
                    HolderName = Text(root, "holderName", 200),
                    ReferenceNumber = Text(root, "referenceNumber", 100),
                    IssuingCountry = DocumentCountries.Normalize(Text(root, "issuingCountry", MaxUncheckedLength)),
                    IssueDate = Date(root, "issueDate"),
                    ExpiryDate = Date(root, "expiryDate"),
                    Stamp = Text(root, "stamp", 20),
                    VehicleRegistration = Text(root, "vehicleRegistration", 20),
                    SigningDate = Date(root, "signingDate"),
                    PenaltyPoints = Points(root)
                }
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement root, string name, int maxLength)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) return null;
        var text = value.GetString()?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        return text.Length <= maxLength ? text : text[..maxLength];
    }

    private static DateOnly? Date(JsonElement root, string name) =>
        DateOnly.TryParseExact(Text(root, name, MaxUncheckedLength), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    private static int? Points(JsonElement root) =>
        root.TryGetProperty("penaltyPoints", out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var points) && points is >= 0 and <= 100
            ? points
            : null;

    /// <returns>The problems reported on a signed form, or null when the output is not usable.</returns>
    public static IReadOnlyList<string>? ParseFormCheck(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                   root.TryGetProperty("problems", out var problems) && problems.ValueKind == JsonValueKind.Array
                ? Problems(root)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<string> Problems(JsonElement root)
    {
        if (!root.TryGetProperty("problems", out var value) || value.ValueKind != JsonValueKind.Array) return [];
        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()?.Trim() ?? string.Empty)
            .Where(item => item.Length > 0)
            .Select(item => item.Length <= MaxProblemLength ? item : item[..MaxProblemLength])
            .Take(MaxProblems)
            .ToList();
    }
}
