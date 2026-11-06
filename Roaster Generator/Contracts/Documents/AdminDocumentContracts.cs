using Roaster_Generator.Enums;
using Roaster_Generator.Services;

namespace Roaster_Generator.Contracts.Documents;

public sealed class AdminDocumentFilters
{
    public string? Search { get; set; }
    public Guid? DocumentTypeId { get; set; }
    public DocumentRowState? State { get; set; }

    /// <summary>Active by default; true includes deactivated employees.</summary>
    public bool IncludeInactive { get; set; }
}

public sealed class AdminDocumentRow
{
    public Guid DocumentTypeId { get; init; }
    public string DocumentTypeName { get; init; } = string.Empty;
    public DocumentRowState State { get; init; }
    public bool IsRequired { get; init; }
    public Guid? DocumentId { get; init; }
    public string? SatisfiedByName { get; init; }
    public string? HolderName { get; init; }
    public string? ReferenceNumber { get; init; }
    public DateOnly? ExpiryDate { get; init; }
    public DateTimeOffset? UploadedAtUtc { get; init; }
    public bool NeedsReview { get; init; }
}

public sealed class AdminDocumentGroup
{
    public Guid EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public string EmployeeNumber { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public int Outstanding { get; init; }
    public int NeedingReview { get; init; }
    public IReadOnlyList<AdminDocumentRow> Documents { get; init; } = [];
}

public sealed class AdminDocumentListResponse
{
    public IReadOnlyList<AdminDocumentGroup> Groups { get; init; } = [];
    public IReadOnlyList<DocumentChecklistType> DocumentTypes { get; init; } = [];
    public bool CanAnalyse { get; init; }
}

public sealed class AdminDocumentFile
{
    public Guid Id { get; init; }
    public int PageIndex { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long Size { get; init; }
}

public sealed class AdminDocumentAuditEntry
{
    public DocumentAuditAction Action { get; init; }
    public string ActorName { get; init; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; init; }
}

public sealed class AdminDocumentDetail
{
    public Guid Id { get; init; }
    public Guid EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public DocumentChecklistType DocumentType { get; init; } = new();
    public DocumentFields Fields { get; init; } = new();
    public bool NeedsReview { get; init; }
    public IReadOnlyList<string> ReviewReasons { get; init; } = [];
    public string? EmployeeNote { get; init; }
    public DateTimeOffset UploadedAtUtc { get; init; }
    public int Revision { get; init; }
    public IReadOnlyList<AdminDocumentFile> Files { get; init; } = [];
    public IReadOnlyList<AdminDocumentAuditEntry> Audit { get; init; } = [];
}

public sealed class DocumentAdminUpdateRequest : DocumentFields
{
    public int Revision { get; set; }
}

public sealed record AdminDocumentDownload(byte[] Content, string ContentType, string FileName);
