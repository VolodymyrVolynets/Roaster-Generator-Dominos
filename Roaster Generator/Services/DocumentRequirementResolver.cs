using Roaster_Generator.Entities;
using Roaster_Generator.Enums;
using Roaster_Generator.Security;

namespace Roaster_Generator.Services;

public enum DocumentRowState
{
    Valid,
    ExpiringSoon,
    WaitingForReview,
    WaitingForManager,
    NeedsNewSignature,
    NeedsVehicleUpdate,
    WrongVehicle,
    NeedsAdminDecision,
    DependsOnPassport,
    NotUploaded,
    Expired
}

/// <summary>The employee's current vehicle, when one is on record.</summary>
public sealed record DocumentVehicleContext(string Registration, bool HasOpenChange);

public sealed record DocumentChecklistRow(
    DocumentType DocumentType,
    DocumentRowState State,
    bool IsRequired,
    EmployeeDocument? Document);

/// <summary>
/// Works out which documents an employee needs and where each one stands. The result never
/// names the employee's category, because driver profile details stay out of the driver workspace.
/// </summary>
public static class DocumentRequirementResolver
{
    public const int ExpiringSoonDays = 30;

    private enum Applicability
    {
        NotRequired,
        NeedsAdminDecision,
        DependsOnPassport,
        Required
    }

    public static IReadOnlySet<DocumentCategory> CategoriesFor(Employee employee, IEnumerable<string> roles)
    {
        var assigned = roles.ToHashSet(StringComparer.Ordinal);
        var categories = new HashSet<DocumentCategory>();
        if (assigned.Contains(RoleNames.Driver) && employee.DriverProfile is { } driver)
            categories.Add(CategoryFor(driver.DriverType, driver.IsOwn));
        if (assigned.Contains(RoleNames.InStore) && employee.InStoreProfile is not null)
            categories.Add(DocumentCategory.InStore);
        if (assigned.Contains(RoleNames.Manager) && employee.ManagerProfile is not null)
            categories.Add(DocumentCategory.Manager);
        return categories;
    }

    /// <summary>Registrations are only tracked for a vehicle the employee owns; e-bikes have none.</summary>
    public static bool TracksVehicle(IEnumerable<DocumentCategory> categories) =>
        categories.Any(category => category is DocumentCategory.CarOwn or DocumentCategory.MopedOwn);

    /// <summary>Required vehicle documents not yet matching the current vehicle.</summary>
    public static int VehicleDocumentsOutstanding(IEnumerable<DocumentChecklistRow> rows) => rows.Count(row =>
        row.IsRequired && DocumentChecks.IsVehicleDocument(row.DocumentType.Code) &&
        row.State is DocumentRowState.NotUploaded or DocumentRowState.NeedsVehicleUpdate or DocumentRowState.WrongVehicle);

    public static DocumentCategory CategoryFor(DriverType type, bool isOwn) => type switch
    {
        DriverType.Moped => isOwn ? DocumentCategory.MopedOwn : DocumentCategory.MopedCompany,
        DriverType.EBike => isOwn ? DocumentCategory.EBikeOwn : DocumentCategory.EBikeCompany,
        _ => isOwn ? DocumentCategory.CarOwn : DocumentCategory.CarCompany
    };

    /// <param name="requirements">Requirement rows with <see cref="DocumentRequirement.DocumentType"/> loaded.</param>
    /// <param name="documentTypes">All document types, needed to show TRC when the LMP route is used.</param>
    /// <param name="documents">The employee's documents with <see cref="EmployeeDocument.DocumentType"/> loaded.</param>
    public static IReadOnlyList<DocumentChecklistRow> Resolve(
        IEnumerable<DocumentCategory> categories,
        IEnumerable<DocumentRequirement> requirements,
        IEnumerable<DocumentType> documentTypes,
        IEnumerable<EmployeeDocument> documents,
        DateOnly today,
        DocumentVehicleContext? vehicle = null)
    {
        var categorySet = categories.ToHashSet();
        var byCode = documents.GroupBy(document => document.DocumentType.Code, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var passport = byCode.GetValueOrDefault(DocumentTypeCodes.Passport);
        var irp = byCode.GetValueOrDefault(DocumentTypeCodes.Irp);

        var rows = new List<DocumentChecklistRow>();
        var consumedCodes = new HashSet<string>(StringComparer.Ordinal);
        var applicable = requirements
            .Where(requirement => categorySet.Contains(requirement.Category) && requirement.DocumentType.IsActive)
            .GroupBy(requirement => requirement.DocumentTypeId)
            .Select(group => (
                Type: group.First().DocumentType,
                Applicability: group.Max(requirement => Evaluate(requirement.Condition, passport, irp))))
            .Where(item => item.Applicability != Applicability.NotRequired)
            .OrderBy(item => item.Type.SortOrder)
            .ToArray();

        foreach (var (type, applicability) in applicable)
        {
            consumedCodes.Add(type.Code);
            var document = byCode.GetValueOrDefault(type.Code);
            if (type.Code == DocumentTypeCodes.Irp)
            {
                rows.AddRange(ResolveImmigrationRows(type, applicability, byCode, documentTypes, consumedCodes, today));
                continue;
            }

            rows.Add(new(type, document is null ? MissingState(applicability) : StateOf(document, today), true, document));
        }

        rows.AddRange(byCode.Values
            .Where(document => !consumedCodes.Contains(document.DocumentType.Code))
            .OrderBy(document => document.DocumentType.SortOrder)
            .Select(document => new DocumentChecklistRow(document.DocumentType, StateOf(document, today), false, document)));
        return vehicle is not null && TracksVehicle(categorySet)
            ? rows.Select(row => ApplyVehicle(row, vehicle)).ToArray()
            : rows;
    }

    /// <summary>
    /// Worked out from the registrations, so clearing a review cannot hide a mismatch. A blank
    /// registration counts as a different vehicle.
    /// </summary>
    private static DocumentChecklistRow ApplyVehicle(DocumentChecklistRow row, DocumentVehicleContext vehicle)
    {
        if (row.Document is not { } document || !DocumentChecks.IsVehicleDocument(row.DocumentType.Code) ||
            DocumentChecks.NormalizeRegistration(document.VehicleRegistration ?? string.Empty) ==
            DocumentChecks.NormalizeRegistration(vehicle.Registration))
            return row;

        return row with { State = vehicle.HasOpenChange ? DocumentRowState.NeedsVehicleUpdate : DocumentRowState.WrongVehicle };
    }

    /// <summary>Tells the employee an outstanding IRP can also be met by a labour market permission.</summary>
    public static string DisplayName(DocumentChecklistRow row) =>
        row.DocumentType.Code == DocumentTypeCodes.Irp && row.IsRequired && row.Document is null
            ? $"{row.DocumentType.Name} or Labour market permission"
            : row.DocumentType.Name;

    public static DocumentRowState StateOf(EmployeeDocument document, DateOnly today)
    {
        var hasExpiry = document.DocumentType.ExpiryRule != DocumentExpiryRule.None;
        if (hasExpiry && document.ExpiryDate < today) return DocumentRowState.Expired;
        if (document.NewSignatureRequested) return DocumentRowState.NeedsNewSignature;
        if (document.AwaitingManagerSignature) return DocumentRowState.WaitingForManager;
        if (document.ReviewStatus == DocumentReviewStatus.NeedsReview) return DocumentRowState.WaitingForReview;
        return hasExpiry && document.ExpiryDate <= today.AddDays(ExpiringSoonDays)
            ? DocumentRowState.ExpiringSoon
            : DocumentRowState.Valid;
    }

    /// <summary>An IRP is satisfied by a valid IRP, or by LMP and TRC together; the better route is shown.</summary>
    private static IEnumerable<DocumentChecklistRow> ResolveImmigrationRows(
        DocumentType irpType,
        Applicability applicability,
        IReadOnlyDictionary<string, EmployeeDocument> byCode,
        IEnumerable<DocumentType> documentTypes,
        ISet<string> consumedCodes,
        DateOnly today)
    {
        var irp = byCode.GetValueOrDefault(DocumentTypeCodes.Irp);
        var lmp = byCode.GetValueOrDefault(DocumentTypeCodes.LabourMarketPermission);
        var trc = byCode.GetValueOrDefault(DocumentTypeCodes.TemporaryResidenceCertificate);
        var irpState = irp is null ? MissingState(applicability) : StateOf(irp, today);

        if (lmp is null)
            return [new(irpType, irpState, true, irp)];

        var lmpRouteState = trc is null
            ? DocumentRowState.NotUploaded
            : Worst(StateOf(lmp, today), StateOf(trc, today));
        if (irp is not null && (trc is null || irpState <= lmpRouteState))
            return [new(irpType, irpState, true, irp)];

        // A superseded IRP stays on the list as an optional row rather than disappearing.
        consumedCodes.Remove(DocumentTypeCodes.Irp);
        consumedCodes.Add(DocumentTypeCodes.LabourMarketPermission);
        consumedCodes.Add(DocumentTypeCodes.TemporaryResidenceCertificate);
        var rows = new List<DocumentChecklistRow> { new(irpType, lmpRouteState, true, lmp) };
        var trcType = trc?.DocumentType ?? documentTypes.FirstOrDefault(
            type => type.Code == DocumentTypeCodes.TemporaryResidenceCertificate);
        if (trcType is not null)
            rows.Add(new(trcType, trc is null ? DocumentRowState.NotUploaded : StateOf(trc, today), true, trc));
        return rows;
    }

    private static Applicability Evaluate(
        DocumentRequirementCondition condition, EmployeeDocument? passport, EmployeeDocument? irp) => condition switch
    {
        DocumentRequirementCondition.NonEuPassport => EvaluateNonEu(passport),
        DocumentRequirementCondition.Stamp2 => EvaluateStamp2(irp),
        _ => Applicability.Required
    };

    private static Applicability EvaluateNonEu(EmployeeDocument? passport)
    {
        if (DocumentCountries.Normalize(passport?.IssuingCountry) is not { } country) return Applicability.DependsOnPassport;
        return DocumentCountries.IsImmigrationExempt(country) ? Applicability.NotRequired : Applicability.Required;
    }

    /// <summary>Exactly Stamp 2 applies the rule. Stamp 2A or an unread stamp goes to a person instead.</summary>
    private static Applicability EvaluateStamp2(EmployeeDocument? irp)
    {
        if (irp is null) return Applicability.NotRequired;
        var stamp = NormalizeStamp(irp.Stamp);
        if (stamp.Length == 0) return Applicability.NeedsAdminDecision;
        if (stamp == "2") return Applicability.Required;
        return stamp.StartsWith('2') ? Applicability.NeedsAdminDecision : Applicability.NotRequired;
    }

    private static string NormalizeStamp(string? stamp) =>
        string.Concat((stamp ?? string.Empty).ToUpperInvariant().Replace("STAMP", string.Empty, StringComparison.Ordinal)
            .Where(char.IsLetterOrDigit));

    private static DocumentRowState MissingState(Applicability applicability) => applicability switch
    {
        Applicability.DependsOnPassport => DocumentRowState.DependsOnPassport,
        Applicability.NeedsAdminDecision => DocumentRowState.NeedsAdminDecision,
        _ => DocumentRowState.NotUploaded
    };

    private static DocumentRowState Worst(DocumentRowState first, DocumentRowState second) =>
        first > second ? first : second;
}
