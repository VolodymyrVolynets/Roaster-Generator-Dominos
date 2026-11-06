using Roaster_Generator.Enums;

namespace Roaster_Generator.Entities;

public sealed class DocumentRequirement
{
    public Guid Id { get; set; }

    public DocumentCategory Category { get; set; }

    public Guid DocumentTypeId { get; set; }

    public DocumentType DocumentType { get; set; } = null!;

    public DocumentRequirementCondition Condition { get; set; } = DocumentRequirementCondition.Always;
}
