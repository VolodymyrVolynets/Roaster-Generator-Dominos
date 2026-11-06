using Microsoft.AspNetCore.Mvc;
using Roaster_Generator.Enums;
using Roaster_Generator.Services;

namespace Roaster_Generator.Contracts.Documents;

/// <summary>The fields of a document, as read by the analysis and confirmed by the person uploading.</summary>
public class DocumentFields
{
    public string? HolderName { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? IssuingCountry { get; set; }
    public DateOnly? IssueDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public string? Stamp { get; set; }
    public string? VehicleRegistration { get; set; }
    public DateOnly? SigningDate { get; set; }
    public int? PenaltyPoints { get; set; }
}

public sealed class DocumentAnalysisResponse
{
    public Guid DraftId { get; init; }
    public DateTimeOffset ExpiresAtUtc { get; init; }
    public bool IsAnalysed { get; init; }
    public Guid? DocumentTypeId { get; init; }
    public string? Summary { get; init; }
    public DocumentFields Fields { get; init; } = new();
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Set when a vehicle document shows a different car and no car change is open.</summary>
    public DocumentVehiclePrompt? VehiclePrompt { get; init; }

    /// <summary>More than one page was provided, or the analysis saw both sides on one.</summary>
    public bool HasBothSides { get; init; }
}

public sealed class DocumentVehiclePrompt
{
    public string CurrentRegistration { get; init; } = string.Empty;
    public string DetectedRegistration { get; init; } = string.Empty;
}

public sealed class VehicleChangeRequest
{
    public string Registration { get; set; } = string.Empty;
}

public sealed class VehicleChangeSummary
{
    public string OldRegistration { get; init; } = string.Empty;
    public string NewRegistration { get; init; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; init; }
    public int DocumentsRemaining { get; init; }
}

public sealed class DocumentVehicleSummary
{
    public string? CurrentRegistration { get; init; }
    public VehicleChangeSummary? OpenChange { get; init; }
}

public sealed class DocumentSubmitRequest : DocumentFields
{
    public Guid DocumentTypeId { get; set; }
    public string? Note { get; set; }
}

public sealed class DocumentSubmitResponse
{
    public Guid DocumentId { get; init; }
    public bool NeedsReview { get; init; }
    public bool ReplacedExisting { get; init; }
}

public sealed class DocumentChecklistItem
{
    public Guid DocumentTypeId { get; init; }
    public string DocumentTypeName { get; init; } = string.Empty;
    public DocumentKind Kind { get; init; }

    /// <summary>A signed form with a template, which the employee needs to sign now.</summary>
    public bool CanSign { get; init; }
    public DocumentRowState State { get; init; }
    public bool IsRequired { get; init; }
    public Guid? DocumentId { get; init; }
    public string? SatisfiedByName { get; init; }
    public string? HolderName { get; init; }
    public string? ReferenceNumber { get; init; }
    public DateOnly? ExpiryDate { get; init; }
    public DateTimeOffset? UploadedAtUtc { get; init; }
    public string? RedoReason { get; init; }
}

public sealed class DocumentChecklistType
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public DocumentKind Kind { get; init; }
    public DocumentExpiryRule ExpiryRule { get; init; }
    public bool HasReference { get; init; }
    public bool RequiresBothSides { get; init; }
}

public sealed class DocumentChecklistResponse
{
    public IReadOnlyList<DocumentChecklistItem> Items { get; init; } = [];

    /// <summary>Null when registrations aren't tracked for this employee.</summary>
    public DocumentVehicleSummary? Vehicle { get; init; }

    /// <summary>Active types the employee can choose when correcting an upload.</summary>
    public IReadOnlyList<DocumentChecklistType> DocumentTypes { get; init; } = [];

    public bool CanAnalyse { get; init; }
}

public sealed class DocumentAnalyseForm
{
    [FromForm(Name = "files")]
    public List<IFormFile> Files { get; set; } = [];

    /// <summary>Set when the upload started from a specific checklist row.</summary>
    [FromForm(Name = "documentTypeId")]
    public Guid? DocumentTypeId { get; set; }
}
