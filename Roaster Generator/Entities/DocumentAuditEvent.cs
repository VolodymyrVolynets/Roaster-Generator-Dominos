using Roaster_Generator.Enums;

namespace Roaster_Generator.Entities;

/// <summary>
/// Who did what to a document. Has no foreign keys so the trail survives deletion, and
/// never holds file contents, reference numbers or other document data.
/// </summary>
public sealed class DocumentAuditEvent
{
    public Guid Id { get; set; }

    public Guid EmployeeId { get; set; }

    public Guid? DocumentId { get; set; }

    public Guid? ActorUserId { get; set; }

    public string ActorName { get; set; } = string.Empty;

    public DocumentAuditAction Action { get; set; }

    public string DocumentTypeCode { get; set; } = string.Empty;

    public DateTimeOffset OccurredAtUtc { get; set; }
}
