using Roaster_Generator.Enums;

namespace Roaster_Generator.Entities;

public sealed class DocumentType
{
    public Guid Id { get; set; }

    /// <summary>Stable identifier used by the requirement rules and the AI analysis. Never changes after creation.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public DocumentKind Kind { get; set; } = DocumentKind.Upload;

    public DocumentExpiryRule ExpiryRule { get; set; } = DocumentExpiryRule.Printed;

    public bool HasReference { get; set; } = true;

    /// <summary>A card with a front and a back, which is only complete with both.</summary>
    public bool RequiresBothSides { get; set; }

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }
}
