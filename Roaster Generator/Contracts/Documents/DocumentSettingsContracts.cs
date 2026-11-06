using Roaster_Generator.Enums;

namespace Roaster_Generator.Contracts.Documents;

public sealed class DocumentTypeRequest
{
    public string Name { get; set; } = string.Empty;
    public DocumentExpiryRule ExpiryRule { get; set; } = DocumentExpiryRule.Printed;
    public bool HasReference { get; set; } = true;
    public bool IsActive { get; set; } = true;
}

public sealed class DocumentTypeResponse
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public DocumentExpiryRule ExpiryRule { get; init; }
    public bool HasReference { get; init; }
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
