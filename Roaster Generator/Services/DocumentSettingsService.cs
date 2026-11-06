using System.Text;
using Microsoft.EntityFrameworkCore;
using Roaster_Generator.Contracts.Documents;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Enums;

namespace Roaster_Generator.Services;

public sealed class DocumentSettingsException(string message) : Exception(message);

/// <summary>Administrator management of the document catalogue and the required list for each employee group.</summary>
public sealed class DocumentSettingsService(AppDbContext db)
{
    private const int SortOrderStep = 10;
    private const int MaxCodeLength = 40;

    public async Task<IReadOnlyList<DocumentTypeResponse>> GetTypesAsync(CancellationToken ct)
    {
        var types = await db.DocumentTypes.AsNoTracking()
            .OrderBy(type => type.SortOrder).ThenBy(type => type.Name)
            .ToListAsync(ct);
        return types.Select(ToResponse).ToArray();
    }

    public async Task<DocumentTypeResponse> CreateTypeAsync(DocumentTypeRequest request, CancellationToken ct)
    {
        var name = request.Name.Trim();
        await EnsureNameIsFreeAsync(name, null, ct);
        var type = new DocumentType
        {
            Code = await UniqueCodeAsync(name, ct),
            Name = name,
            ExpiryRule = request.ExpiryRule,
            HasReference = request.HasReference,
            IsActive = request.IsActive,
            SortOrder = (await db.DocumentTypes.MaxAsync(item => (int?)item.SortOrder, ct) ?? 0) + SortOrderStep
        };
        db.DocumentTypes.Add(type);
        await db.SaveChangesAsync(ct);
        return ToResponse(type);
    }

    /// <returns>The updated type, or null when it does not exist.</returns>
    public async Task<DocumentTypeResponse?> UpdateTypeAsync(Guid id, DocumentTypeRequest request, CancellationToken ct)
    {
        var type = await db.DocumentTypes.SingleOrDefaultAsync(item => item.Id == id, ct);
        if (type is null) return null;

        var name = request.Name.Trim();
        await EnsureNameIsFreeAsync(name, id, ct);
        type.Name = name;
        type.ExpiryRule = request.ExpiryRule;
        type.HasReference = request.HasReference;
        type.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);
        return ToResponse(type);
    }

    /// <returns>False when the type does not exist.</returns>
    public async Task<bool> DeleteTypeAsync(Guid id, CancellationToken ct)
    {
        var type = await db.DocumentTypes.SingleOrDefaultAsync(item => item.Id == id, ct);
        if (type is null) return false;
        if (DocumentTypeCodes.BuiltIn.Contains(type.Code))
            throw new DocumentSettingsException("This document type is built in. Turn it off instead of deleting it.");
        if (await db.DocumentRequirements.AnyAsync(item => item.DocumentTypeId == id, ct) ||
            await db.EmployeeDocuments.AnyAsync(item => item.DocumentTypeId == id, ct))
            throw new DocumentSettingsException("This document type is in use. Turn it off instead of deleting it.");

        db.DocumentTypes.Remove(type);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<DocumentRequirementListResponse>> GetListsAsync(CancellationToken ct)
    {
        var requirements = await db.DocumentRequirements.AsNoTracking()
            .OrderBy(item => item.DocumentType.SortOrder)
            .ToListAsync(ct);
        var byCategory = requirements.ToLookup(item => item.Category);
        return Enum.GetValues<DocumentCategory>().Select(category => ToResponse(category, byCategory[category])).ToArray();
    }

    public async Task<DocumentRequirementListResponse> SaveListAsync(
        DocumentCategory category, DocumentRequirementListRequest request, CancellationToken ct)
    {
        var requestedIds = request.Items.Select(item => item.DocumentTypeId).ToArray();
        var knownCount = await db.DocumentTypes.CountAsync(type => requestedIds.Contains(type.Id), ct);
        if (knownCount != requestedIds.Length)
            throw new DocumentSettingsException("One of these document types no longer exists. Reload and try again.");

        db.DocumentRequirements.RemoveRange(
            await db.DocumentRequirements.Where(item => item.Category == category).ToListAsync(ct));
        var saved = request.Items.Select(item => new DocumentRequirement
        {
            Category = category,
            DocumentTypeId = item.DocumentTypeId,
            Condition = item.Condition
        }).ToArray();
        db.DocumentRequirements.AddRange(saved);
        await db.SaveChangesAsync(ct);
        return ToResponse(category, saved);
    }

    private async Task EnsureNameIsFreeAsync(string name, Guid? ignoredId, CancellationToken ct)
    {
        var lowered = name.ToLower();
        if (await db.DocumentTypes.AnyAsync(type => type.Id != ignoredId && type.Name.ToLower() == lowered, ct))
            throw new DocumentSettingsException("A document type with this name already exists.");
    }

    private async Task<string> UniqueCodeAsync(string name, CancellationToken ct)
    {
        var slug = Slug(name);
        var existing = await db.DocumentTypes.Select(type => type.Code).ToListAsync(ct);
        var taken = existing.ToHashSet(StringComparer.Ordinal);
        var code = slug;
        for (var suffix = 2; taken.Contains(code); suffix++)
        {
            var ending = $"-{suffix}";
            code = slug[..Math.Min(slug.Length, MaxCodeLength - ending.Length)] + ending;
        }

        return code;
    }

    private static string Slug(string name)
    {
        var slug = new StringBuilder(name.Length);
        foreach (var character in name.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character)) slug.Append(character);
            else if (slug.Length > 0 && slug[^1] != '-') slug.Append('-');
        }

        var trimmed = slug.ToString().Trim('-');
        trimmed = trimmed[..Math.Min(trimmed.Length, MaxCodeLength)].TrimEnd('-');
        return trimmed.Length > 0 ? trimmed : "document";
    }

    private static DocumentTypeResponse ToResponse(DocumentType type) => new()
    {
        Id = type.Id,
        Code = type.Code,
        Name = type.Name,
        ExpiryRule = type.ExpiryRule,
        HasReference = type.HasReference,
        IsActive = type.IsActive,
        SortOrder = type.SortOrder,
        IsBuiltIn = DocumentTypeCodes.BuiltIn.Contains(type.Code)
    };

    private static DocumentRequirementListResponse ToResponse(
        DocumentCategory category, IEnumerable<DocumentRequirement> requirements) => new()
    {
        Category = category,
        Items = requirements
            .Select(item => new DocumentRequirementItem { DocumentTypeId = item.DocumentTypeId, Condition = item.Condition })
            .ToArray()
    };
}
