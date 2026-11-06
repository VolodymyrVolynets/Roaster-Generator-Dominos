namespace Roaster_Generator.Entities;

/// <summary>The blank form behind a signed document type. It holds no personal data, so it is not encrypted.</summary>
public sealed class DocumentTemplate
{
    public Guid DocumentTypeId { get; set; }

    public DocumentType DocumentType { get; set; } = null!;

    public byte[] Content { get; set; } = [];

    public string FileName { get; set; } = string.Empty;

    /// <summary>The form's fields and where each value comes from, as JSON.</summary>
    public string Fields { get; set; } = "[]";

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
