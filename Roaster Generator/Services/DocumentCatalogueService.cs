using Microsoft.EntityFrameworkCore;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Enums;

namespace Roaster_Generator.Services;

public static class DocumentTypeCodes
{
    public const string Passport = "passport";
    public const string DrivingLicence = "driving-licence";
    public const string Irp = "irp";
    public const string LabourMarketPermission = "lmp";
    public const string TemporaryResidenceCertificate = "trc";
    public const string Spf = "spf";
    public const string FoodHandler = "food-handler";
    public const string Nct = "nct";
    public const string MotorTax = "motor-tax";
    public const string Insurance = "insurance";
    public const string Obu = "obu";
    public const string PenaltyPoints = "pp";
    public const string CollegeLetter = "college-letter";

    /// <summary>Codes the requirement rules and checks refer to, so these types cannot be deleted.</summary>
    public static readonly IReadOnlySet<string> BuiltIn = new HashSet<string>(StringComparer.Ordinal)
    {
        Passport, DrivingLicence, Irp, LabourMarketPermission, TemporaryResidenceCertificate, Spf,
        FoodHandler, Nct, MotorTax, Insurance, Obu, PenaltyPoints, CollegeLetter
    };
}

public sealed class DocumentCatalogueService(AppDbContext db)
{
    private const DocumentRequirementCondition Always = DocumentRequirementCondition.Always;
    private const DocumentRequirementCondition NonEu = DocumentRequirementCondition.NonEuPassport;
    private const DocumentRequirementCondition Stamp2 = DocumentRequirementCondition.Stamp2;

    private static readonly (string Code, string Name, DocumentExpiryRule Rule, bool HasReference)[] Catalogue =
    [
        (DocumentTypeCodes.Passport, "Passport", DocumentExpiryRule.Printed, true),
        (DocumentTypeCodes.DrivingLicence, "Driving licence", DocumentExpiryRule.Printed, true),
        (DocumentTypeCodes.Irp, "IRP", DocumentExpiryRule.Printed, true),
        (DocumentTypeCodes.LabourMarketPermission, "Labour market permission", DocumentExpiryRule.Printed, true),
        (DocumentTypeCodes.TemporaryResidenceCertificate, "TRC", DocumentExpiryRule.Printed, true),
        (DocumentTypeCodes.CollegeLetter, "College letter", DocumentExpiryRule.None, false),
        (DocumentTypeCodes.Spf, "Service Provider Form", DocumentExpiryRule.None, false),
        (DocumentTypeCodes.FoodHandler, "Food handler", DocumentExpiryRule.None, false),
        (DocumentTypeCodes.PenaltyPoints, "Penalty points confirmation", DocumentExpiryRule.PenaltyPoints, false),
        (DocumentTypeCodes.Nct, "NCT", DocumentExpiryRule.Printed, false),
        (DocumentTypeCodes.MotorTax, "Motor tax", DocumentExpiryRule.Printed, false),
        (DocumentTypeCodes.Insurance, "Insurance", DocumentExpiryRule.Printed, false),
        (DocumentTypeCodes.Obu, "Business Use Insurance", DocumentExpiryRule.None, false)
    ];

    private static readonly DocumentCategory[] DriverCategories =
    [
        DocumentCategory.CarOwn, DocumentCategory.CarCompany,
        DocumentCategory.MopedOwn, DocumentCategory.MopedCompany,
        DocumentCategory.EBikeOwn, DocumentCategory.EBikeCompany
    ];

    private static readonly DocumentCategory[] StoreStaffCategories = [DocumentCategory.InStore, DocumentCategory.Manager];

    private static readonly (string Code, DocumentRequirementCondition Condition)[] DriverList =
    [
        (DocumentTypeCodes.Passport, Always),
        (DocumentTypeCodes.DrivingLicence, Always),
        (DocumentTypeCodes.Irp, NonEu),
        (DocumentTypeCodes.CollegeLetter, Stamp2),
        (DocumentTypeCodes.Spf, Always),
        (DocumentTypeCodes.FoodHandler, Always),
        (DocumentTypeCodes.PenaltyPoints, Always),
        (DocumentTypeCodes.Nct, Always),
        (DocumentTypeCodes.MotorTax, Always),
        (DocumentTypeCodes.Insurance, Always),
        (DocumentTypeCodes.Obu, Always)
    ];

    private static readonly (string Code, DocumentRequirementCondition Condition)[] StoreStaffList =
    [
        (DocumentTypeCodes.Passport, Always),
        (DocumentTypeCodes.Irp, NonEu),
        (DocumentTypeCodes.CollegeLetter, Stamp2),
        (DocumentTypeCodes.Spf, Always),
        (DocumentTypeCodes.FoodHandler, Always)
    ];

    /// <summary>Seeds once, only into an empty catalogue, so an administrator's later changes are never undone.</summary>
    public async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (await db.DocumentTypes.AnyAsync(ct)) return;

        var types = Catalogue.Select((item, index) => new DocumentType
        {
            Id = Guid.NewGuid(),
            Code = item.Code,
            Name = item.Name,
            ExpiryRule = item.Rule,
            HasReference = item.HasReference,
            SortOrder = (index + 1) * 10
        }).ToDictionary(type => type.Code, StringComparer.Ordinal);

        db.DocumentTypes.AddRange(types.Values);
        foreach (var category in DriverCategories)
            db.DocumentRequirements.AddRange(ToRequirements(category, DriverList, types));
        foreach (var category in StoreStaffCategories)
            db.DocumentRequirements.AddRange(ToRequirements(category, StoreStaffList, types));
        await db.SaveChangesAsync(ct);
    }

    private static IEnumerable<DocumentRequirement> ToRequirements(
        DocumentCategory category,
        IEnumerable<(string Code, DocumentRequirementCondition Condition)> items,
        IReadOnlyDictionary<string, DocumentType> types) =>
        items.Select(item => new DocumentRequirement
        {
            Category = category,
            DocumentTypeId = types[item.Code].Id,
            Condition = item.Condition
        });
}
