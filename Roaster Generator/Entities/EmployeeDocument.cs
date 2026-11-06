using Roaster_Generator.Enums;

namespace Roaster_Generator.Entities;

/// <summary>The current document of one type for one employee. File contents live in <see cref="EmployeeDocumentFile"/>.</summary>
public sealed class EmployeeDocument
{
    public Guid Id { get; set; }

    public Guid EmployeeId { get; set; }

    public Employee Employee { get; set; } = null!;

    public Guid DocumentTypeId { get; set; }

    public DocumentType DocumentType { get; set; } = null!;

    public string? HolderName { get; set; }

    public string? ReferenceNumber { get; set; }

    /// <summary>ISO 3166-1 alpha-2 code of the issuing country.</summary>
    public string? IssuingCountry { get; set; }

    public DateOnly? IssueDate { get; set; }

    /// <summary>The printed expiry, or the calculated due date for rule-based documents.</summary>
    public DateOnly? ExpiryDate { get; set; }

    public string? Stamp { get; set; }

    public string? VehicleRegistration { get; set; }

    public DateOnly? SigningDate { get; set; }

    public int? PenaltyPoints { get; set; }

    public DocumentReviewStatus ReviewStatus { get; set; } = DocumentReviewStatus.Clear;

    public List<string> ReviewReasons { get; set; } = [];

    public string? EmployeeNote { get; set; }

    /// <summary>An administrator has asked the employee to sign or upload this document again.</summary>
    public bool NewSignatureRequested { get; set; }

    /// <summary>Why it has to be redone, shown to the employee. Replacing the document removes it.</summary>
    public string? RedoReason { get; set; }

    /// <summary>The employee has signed a form that also needs a manager's signature.</summary>
    public bool AwaitingManagerSignature { get; set; }

    public string? ManagerSignedByName { get; set; }

    public DateTimeOffset? ManagerSignedAtUtc { get; set; }

    public DateTimeOffset UploadedAtUtc { get; set; }

    public Guid? UploadedByUserId { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public int Revision { get; set; }

    public ICollection<EmployeeDocumentFile> Files { get; set; } = new List<EmployeeDocumentFile>();
}
