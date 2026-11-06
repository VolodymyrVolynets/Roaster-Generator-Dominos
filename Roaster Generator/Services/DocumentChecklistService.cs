using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Roaster_Generator.Contracts.Documents;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Enums;

namespace Roaster_Generator.Services;

public sealed class DocumentChecklistService(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    DocumentFileProtector protector,
    DocumentVehicleService vehicles,
    TimeProvider timeProvider)
{
    public async Task<DocumentChecklistResponse> GetAsync(Guid employeeId, CancellationToken ct)
    {
        var employee = await db.Employees.AsNoTracking()
            .Include(item => item.DriverProfile).Include(item => item.InStoreProfile).Include(item => item.ManagerProfile)
            .SingleAsync(item => item.Id == employeeId, ct);
        var user = await userManager.Users.AsNoTracking().SingleOrDefaultAsync(item => item.EmployeeId == employeeId, ct);
        var roles = user is null ? [] : await userManager.GetRolesAsync(user);

        var requirements = await db.DocumentRequirements.AsNoTracking().Include(item => item.DocumentType).ToListAsync(ct);
        var types = await db.DocumentTypes.AsNoTracking().ToListAsync(ct);
        var documents = await db.EmployeeDocuments.AsNoTracking()
            .Include(item => item.DocumentType)
            .Where(item => item.EmployeeId == employeeId)
            .ToListAsync(ct);

        var vehicle = await vehicles.GetAsync(employeeId, ct);
        var rows = DocumentRequirementResolver.Resolve(
            DocumentRequirementResolver.CategoriesFor(employee, roles), requirements, types, documents,
            DocumentDates.Today(timeProvider), vehicle.Context);
        var templates = (await db.DocumentTemplates.AsNoTracking().ToListAsync(ct))
            .Where(item => DocumentSigningService.IsReady(item.Fields)).Select(item => item.DocumentTypeId).ToHashSet();
        return new DocumentChecklistResponse
        {
            Items = rows.Select(row => ToItem(row, templates)).ToArray(),
            Vehicle = ToVehicleSummary(vehicle, rows),
            DocumentTypes = types.Where(type => type.IsActive && type.Kind == DocumentKind.Upload).OrderBy(type => type.SortOrder)
                .Select(type => new DocumentChecklistType
                {
                    Id = type.Id,
                    Code = type.Code,
                    Name = type.Name,
                    Kind = type.Kind,
                    ExpiryRule = type.ExpiryRule,
                    HasReference = type.HasReference,
                    RequiresBothSides = type.RequiresBothSides
                }).ToArray(),
            CanAnalyse = protector.IsConfigured
        };
    }

    internal static DocumentVehicleSummary? ToVehicleSummary(
        EmployeeVehicleState vehicle, IReadOnlyList<DocumentChecklistRow> rows) => !vehicle.TracksVehicle
        ? null
        : new DocumentVehicleSummary
        {
            CurrentRegistration = vehicle.Registration,
            OpenChange = vehicle.OpenChange is not { } change
                ? null
                : new VehicleChangeSummary
                {
                    OldRegistration = change.OldRegistration,
                    NewRegistration = change.NewRegistration,
                    StartedAtUtc = change.StartedAtUtc,
                    DocumentsRemaining = DocumentRequirementResolver.VehicleDocumentsOutstanding(rows)
                }
        };

    private static readonly DocumentRowState[] SignableStates =
    [
        DocumentRowState.NotUploaded, DocumentRowState.Expired, DocumentRowState.ExpiringSoon,
        DocumentRowState.NeedsNewSignature, DocumentRowState.NeedsVehicleUpdate, DocumentRowState.WrongVehicle
    ];

    private static DocumentChecklistItem ToItem(DocumentChecklistRow row, IReadOnlySet<Guid> templates) => new()
    {
        DocumentTypeId = row.DocumentType.Id,
        DocumentTypeName = DocumentRequirementResolver.DisplayName(row),
        Kind = row.DocumentType.Kind,
        CanSign = row.DocumentType.Kind == DocumentKind.Sign && row.DocumentType.IsActive &&
                  templates.Contains(row.DocumentType.Id) && SignableStates.Contains(row.State),
        State = row.State,
        IsRequired = row.IsRequired,
        DocumentId = row.Document?.Id,
        SatisfiedByName = row.Document is { } document && document.DocumentTypeId != row.DocumentType.Id
            ? document.DocumentType.Name
            : null,
        HolderName = row.Document?.HolderName,
        ReferenceNumber = row.Document?.ReferenceNumber,
        ExpiryDate = row.Document?.ExpiryDate,
        UploadedAtUtc = row.Document?.UploadedAtUtc
    };
}
