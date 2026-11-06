using Roaster_Generator.Enums;
using Roaster_Generator.Services;

namespace Roaster_Generator.Contracts.Documents;

public sealed class DocumentTypeRequest
{
    public string Name { get; set; } = string.Empty;
    public DocumentKind Kind { get; set; } = DocumentKind.Upload;
    public DocumentExpiryRule ExpiryRule { get; set; } = DocumentExpiryRule.Printed;
    public bool HasReference { get; set; } = true;
    public bool RequiresBothSides { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class DocumentTypeResponse
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public DocumentKind Kind { get; init; }
    public bool HasTemplate { get; init; }
    public DocumentExpiryRule ExpiryRule { get; init; }
    public bool HasReference { get; init; }
    public bool RequiresBothSides { get; init; }
    public bool IsActive { get; init; }
    public int SortOrder { get; init; }
    public bool IsBuiltIn { get; init; }
}

public sealed class DocumentRequirementItem
{
    public Guid DocumentTypeId { get; set; }
    public DocumentRequirementCondition Condition { get; set; } = DocumentRequirementCondition.Always;
}

public sealed class DocumentRequirementListRequest
{
    public List<DocumentRequirementItem> Items { get; set; } = [];
}

public sealed class DocumentRequirementListResponse
{
    public DocumentCategory Category { get; init; }
    public IReadOnlyList<DocumentRequirementItem> Items { get; init; } = [];
}

public sealed class DocumentTemplateField
{
    public string Name { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool IsCheckbox { get; set; }
    public DocumentFieldSource Source { get; set; } = DocumentFieldSource.Ask;
    public bool Required { get; set; } = true;
}

public sealed class DocumentTemplateResponse
{
    public string FileName { get; init; } = string.Empty;
    public IReadOnlyList<DocumentTemplateField> Fields { get; init; } = [];
}

public sealed class DocumentTemplateFieldsRequest
{
    public List<DocumentTemplateField> Fields { get; set; } = [];
}

public sealed class DocumentSignField
{
    public string Name { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public bool IsCheckbox { get; init; }
    public bool Required { get; init; }
    public bool ReadOnly { get; init; }
    public string? Value { get; init; }
}

public sealed class DocumentSignForm
{
    public Guid DocumentTypeId { get; init; }
    public string DocumentTypeName { get; init; } = string.Empty;
    public IReadOnlyList<DocumentSignField> Fields { get; init; } = [];
}

public sealed class DocumentSignRequest
{
    public Dictionary<string, string?> Values { get; set; } = [];

    /// <summary>The drawn signature as a base64 PNG, without a data URL prefix.</summary>
    public string Signature { get; set; } = string.Empty;
}

public sealed class DocumentSignOffSummary
{
    public Guid DocumentId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public string DocumentTypeName { get; init; } = string.Empty;
    public DateTimeOffset SignedAtUtc { get; init; }
}

public sealed class DocumentSignOffForm
{
    public Guid DocumentId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public string DocumentTypeName { get; init; } = string.Empty;
    public string ManagerName { get; init; } = string.Empty;
    public bool AsksForManagerName { get; init; }
}

public sealed class DocumentSignOffRequest
{
    public string? ManagerName { get; set; }

    /// <summary>The drawn signature as a base64 PNG, without a data URL prefix.</summary>
    public string Signature { get; set; } = string.Empty;
}

public sealed class DocumentAutoSignatureResponse
{
    public string ManagerName { get; init; } = string.Empty;
    public DateTimeOffset SavedAtUtc { get; init; }
}

public sealed record DocumentSignOffActor(DocumentActor Actor, Guid? EmployeeId, string DisplayName);
