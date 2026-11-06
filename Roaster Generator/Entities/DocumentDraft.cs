namespace Roaster_Generator.Entities;

/// <summary>Analysed files waiting for the employee to confirm or discard them.</summary>
public sealed class DocumentDraft
{
    public Guid Id { get; set; }

    public Guid EmployeeId { get; set; }

    public Employee Employee { get; set; } = null!;

    public Guid? CreatedByUserId { get; set; }

    /// <summary>The analysis as JSON, or null when the file could not be analysed.</summary>
    public string? AnalysisResult { get; set; }

    /// <summary>Images plus PDF pages, used to tell whether both sides of a card were provided.</summary>
    public int PageCount { get; set; } = 1;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public ICollection<EmployeeDocumentFile> Files { get; set; } = new List<EmployeeDocumentFile>();
}
