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

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public ICollection<EmployeeDocumentFile> Files { get; set; } = new List<EmployeeDocumentFile>();
}
