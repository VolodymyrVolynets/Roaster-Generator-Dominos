using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Roaster_Generator.Contracts.Documents;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Enums;

namespace Roaster_Generator.Services;

public sealed class DocumentConflictException : Exception;

/// <summary>Administrator access to every employee's documents. Each file read is recorded in the audit trail.</summary>
public sealed class AdminDocumentService(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    DocumentFileProtector protector,
    DocumentVehicleService vehicles,
    TimeProvider timeProvider,
    ILogger<AdminDocumentService> logger,
    ApplicationEventPublisher? events = null)
{
    private const int AuditEntriesShown = 50;

    public async Task<AdminDocumentListResponse> ListAsync(AdminDocumentFilters filters, CancellationToken ct)
    {
        var employees = await db.Employees.AsNoTracking()
            .Include(item => item.DriverProfile).Include(item => item.InStoreProfile).Include(item => item.ManagerProfile)
            .Where(item => !item.IsDeleted && (filters.IncludeInactive || item.IsActive))
            .OrderBy(item => item.FirstName).ThenBy(item => item.LastName)
            .ToListAsync(ct);
        var roles = await RolesByEmployeeAsync(ct);
        var requirements = await db.DocumentRequirements.AsNoTracking().Include(item => item.DocumentType).ToListAsync(ct);
        var types = await db.DocumentTypes.AsNoTracking().ToListAsync(ct);
        var documents = (await db.EmployeeDocuments.AsNoTracking().Include(item => item.DocumentType).ToListAsync(ct))
            .ToLookup(item => item.EmployeeId);
        var today = DocumentDates.Today(timeProvider);
        var search = filters.Search?.Trim();
        var registrations = await db.EmployeeVehicles.AsNoTracking()
            .ToDictionaryAsync(item => item.EmployeeId, item => item.Registration, ct);
        var openChanges = await db.VehicleChanges.AsNoTracking()
            .Where(item => item.Status == VehicleChangeStatus.Open)
            .ToDictionaryAsync(item => item.EmployeeId, ct);

        var groups = new List<AdminDocumentGroup>();
        foreach (var employee in employees)
        {
            var categories = DocumentRequirementResolver.CategoriesFor(employee, roles[employee.Id]);
            var vehicle = new EmployeeVehicleState(
                DocumentRequirementResolver.TracksVehicle(categories),
                registrations.GetValueOrDefault(employee.Id),
                openChanges.GetValueOrDefault(employee.Id));
            var rows = DocumentRequirementResolver.Resolve(
                categories, requirements, types, documents[employee.Id], today, vehicle.Context);
            var name = $"{employee.FirstName} {employee.LastName}".Trim();
            var matchesEmployee = string.IsNullOrEmpty(search) || Contains(name, search) || Contains(employee.EmployeeNumber, search);
            var shown = rows
                .Where(row => filters.DocumentTypeId is not Guid typeId || row.DocumentType.Id == typeId)
                .Where(row => filters.State is not DocumentRowState state || row.State == state)
                .Where(row => matchesEmployee || Contains(row.Document?.ReferenceNumber, search!) || Contains(row.Document?.HolderName, search!))
                .ToArray();
            var unfiltered = filters.DocumentTypeId is null && filters.State is null && matchesEmployee;
            if (shown.Length == 0 && !unfiltered) continue;

            groups.Add(new AdminDocumentGroup
            {
                EmployeeId = employee.Id,
                EmployeeName = name,
                EmployeeNumber = employee.EmployeeNumber,
                IsActive = employee.IsActive,
                Outstanding = rows.Count(row => row.IsRequired && row.State is DocumentRowState.NotUploaded
                    or DocumentRowState.Expired or DocumentRowState.WrongVehicle or DocumentRowState.NeedsVehicleUpdate),
                Vehicle = DocumentChecklistService.ToVehicleSummary(vehicle, rows),
                NeedingReview = rows.Count(row => row.Document?.ReviewStatus == DocumentReviewStatus.NeedsReview),
                Documents = shown.Select(ToRow).ToArray()
            });
        }

        return new AdminDocumentListResponse
        {
            Groups = groups,
            DocumentTypes = types.Where(type => type.IsActive).OrderBy(type => type.SortOrder).Select(ToType).ToArray(),
            CanAnalyse = protector.IsConfigured
        };
    }

    public async Task<AdminDocumentDetail?> GetAsync(Guid id, CancellationToken ct)
    {
        var document = await db.EmployeeDocuments.AsNoTracking()
            .Include(item => item.DocumentType).Include(item => item.Employee)
            .SingleOrDefaultAsync(item => item.Id == id, ct);
        if (document is null) return null;

        var files = await db.EmployeeDocumentFiles.AsNoTracking()
            .Where(file => file.DocumentId == id)
            .OrderBy(file => file.PageIndex)
            .Select(file => new AdminDocumentFile
            {
                Id = file.Id, PageIndex = file.PageIndex, FileName = file.FileName, ContentType = file.ContentType, Size = file.Size
            })
            .ToListAsync(ct);
        var audit = await db.DocumentAuditEvents.AsNoTracking()
            .Where(item => item.DocumentId == id)
            .OrderByDescending(item => item.OccurredAtUtc)
            .Take(AuditEntriesShown)
            .Select(item => new AdminDocumentAuditEntry { Action = item.Action, ActorName = item.ActorName, OccurredAtUtc = item.OccurredAtUtc })
            .ToListAsync(ct);
        return ToDetail(document, files, audit);
    }

    /// <returns>The decrypted file, or null when the document has no such file.</returns>
    /// <exception cref="CryptographicException">The stored file cannot be decrypted.</exception>
    public async Task<AdminDocumentDownload?> ReadFileAsync(Guid documentId, Guid fileId, bool preview, DocumentActor actor, CancellationToken ct)
    {
        var file = await db.EmployeeDocumentFiles.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == fileId && item.DocumentId == documentId, ct);
        if (file is null) return null;

        var document = await db.EmployeeDocuments.AsNoTracking()
            .Include(item => item.DocumentType).Include(item => item.Employee)
            .SingleAsync(item => item.Id == documentId, ct);
        var pageCount = await db.EmployeeDocumentFiles.CountAsync(item => item.DocumentId == documentId, ct);

        byte[] content;
        try
        {
            content = protector.Unprotect(file.Id, new ProtectedDocumentFile(
                file.EncryptedContent, file.Nonce, file.Tag, file.KeyId));
        }
        catch (CryptographicException)
        {
            logger.LogError("Document file {FileId} could not be decrypted.", fileId);
            throw;
        }

        db.DocumentAuditEvents.Add(Audit(document.EmployeeId, documentId, actor,
            preview ? DocumentAuditAction.Viewed : DocumentAuditAction.Downloaded, document.DocumentType.Code));
        await db.SaveChangesAsync(ct);
        return new AdminDocumentDownload(content, file.ContentType, DocumentFileNames.Build(
            document.Employee.FirstName, document, Path.GetExtension(file.FileName), file.PageIndex, pageCount));
    }

    /// <summary>Saving means an administrator has checked the document, so it always clears the review flag.</summary>
    /// <returns>The updated document, or null when it does not exist.</returns>
    /// <exception cref="DocumentConflictException">Someone else changed the document first.</exception>
    public async Task<AdminDocumentDetail?> UpdateAsync(
        Guid id, DocumentAdminUpdateRequest request, DocumentActor actor, CancellationToken ct)
    {
        var document = await db.EmployeeDocuments.Include(item => item.DocumentType)
            .SingleOrDefaultAsync(item => item.Id == id, ct);
        if (document is null) return null;
        if (document.Revision != request.Revision) throw new DocumentConflictException();

        var fields = DocumentDraftService.Normalize(request);
        var wasFlagged = document.ReviewStatus == DocumentReviewStatus.NeedsReview;
        document.HolderName = fields.HolderName;
        document.ReferenceNumber = fields.ReferenceNumber;
        document.IssuingCountry = fields.IssuingCountry;
        document.IssueDate = fields.IssueDate;
        document.ExpiryDate = DocumentChecks.ResolveExpiry(
            document.DocumentType.ExpiryRule, fields.ExpiryDate, fields.SigningDate, fields.PenaltyPoints);
        document.Stamp = fields.Stamp;
        document.VehicleRegistration = fields.VehicleRegistration;
        document.SigningDate = fields.SigningDate;
        document.PenaltyPoints = fields.PenaltyPoints;
        document.ReviewStatus = DocumentReviewStatus.Clear;
        document.ReviewReasons = [];
        document.UpdatedAtUtc = timeProvider.GetUtcNow();
        document.Revision++;
        db.DocumentAuditEvents.Add(Audit(document.EmployeeId, document.Id, actor,
            wasFlagged ? DocumentAuditAction.Reviewed : DocumentAuditAction.Edited, document.DocumentType.Code));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DocumentConflictException();
        }

        if (DocumentChecks.IsVehicleDocument(document.DocumentType.Code))
            await vehicles.DocumentSavedAsync(document.EmployeeId, actor, fields.VehicleRegistration, ct);
        if (events is not null) await events.DocumentsChangedAsync(document.EmployeeId, ct);
        return await GetAsync(id, ct);
    }

    /// <returns>The document, or null when it is missing or is not a signed form.</returns>
    public async Task<AdminDocumentDetail?> RequestSignatureAsync(Guid id, DocumentActor actor, CancellationToken ct)
    {
        var document = await db.EmployeeDocuments.Include(item => item.DocumentType)
            .SingleOrDefaultAsync(item => item.Id == id && item.DocumentType.Kind == DocumentKind.Sign, ct);
        if (document is null) return null;

        document.NewSignatureRequested = true;
        document.UpdatedAtUtc = timeProvider.GetUtcNow();
        document.Revision++;
        db.DocumentAuditEvents.Add(Audit(document.EmployeeId, document.Id, actor, DocumentAuditAction.Edited, document.DocumentType.Code));
        await db.SaveChangesAsync(ct);
        if (events is not null) await events.DocumentsChangedAsync(document.EmployeeId, ct);
        return await GetAsync(id, ct);
    }

    /// <returns>False when the document does not exist.</returns>
    public async Task<bool> DeleteAsync(Guid id, DocumentActor actor, CancellationToken ct)
    {
        var document = await db.EmployeeDocuments
            .Include(item => item.DocumentType).Include(item => item.Files)
            .SingleOrDefaultAsync(item => item.Id == id, ct);
        if (document is null) return false;

        db.EmployeeDocuments.Remove(document);
        db.DocumentAuditEvents.Add(Audit(document.EmployeeId, document.Id, actor, DocumentAuditAction.Deleted, document.DocumentType.Code));
        await db.SaveChangesAsync(ct);
        if (events is not null) await events.DocumentsChangedAsync(document.EmployeeId, ct);
        return true;
    }

    private async Task<ILookup<Guid, string>> RolesByEmployeeAsync(CancellationToken ct)
    {
        var rows = await (
            from user in userManager.Users
            where user.EmployeeId != null
            join userRole in db.UserRoles on user.Id equals userRole.UserId
            join role in db.Roles on userRole.RoleId equals role.Id
            select new { EmployeeId = user.EmployeeId!.Value, role.Name }).ToListAsync(ct);
        return rows.Where(row => row.Name is not null).ToLookup(row => row.EmployeeId, row => row.Name!);
    }

    private static bool Contains(string? value, string search) =>
        value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;

    private DocumentAuditEvent Audit(
        Guid employeeId, Guid documentId, DocumentActor actor, DocumentAuditAction action, string code) => new()
    {
        EmployeeId = employeeId,
        DocumentId = documentId,
        ActorUserId = actor.UserId,
        ActorName = actor.Name,
        Action = action,
        DocumentTypeCode = code,
        OccurredAtUtc = timeProvider.GetUtcNow()
    };

    private static AdminDocumentRow ToRow(DocumentChecklistRow row) => new()
    {
        DocumentTypeId = row.DocumentType.Id,
        DocumentTypeName = DocumentRequirementResolver.DisplayName(row),
        Kind = row.DocumentType.Kind,
        State = row.State,
        IsRequired = row.IsRequired,
        DocumentId = row.Document?.Id,
        SatisfiedByName = row.Document is { } document && document.DocumentTypeId != row.DocumentType.Id
            ? document.DocumentType.Name
            : null,
        HolderName = row.Document?.HolderName,
        ReferenceNumber = row.Document?.ReferenceNumber,
        ExpiryDate = row.Document?.ExpiryDate,
        UploadedAtUtc = row.Document?.UploadedAtUtc,
        NeedsReview = row.Document?.ReviewStatus == DocumentReviewStatus.NeedsReview
    };

    private static DocumentChecklistType ToType(DocumentType type) => new()
    {
        Id = type.Id, Code = type.Code, Name = type.Name, Kind = type.Kind, ExpiryRule = type.ExpiryRule, HasReference = type.HasReference,
        RequiresBothSides = type.RequiresBothSides
    };

    private static AdminDocumentDetail ToDetail(
        EmployeeDocument document, IReadOnlyList<AdminDocumentFile> files, IReadOnlyList<AdminDocumentAuditEntry> audit) => new()
    {
        Id = document.Id,
        EmployeeId = document.EmployeeId,
        EmployeeName = $"{document.Employee.FirstName} {document.Employee.LastName}".Trim(),
        DocumentType = ToType(document.DocumentType),
        Fields = new DocumentFields
        {
            HolderName = document.HolderName,
            ReferenceNumber = document.ReferenceNumber,
            IssuingCountry = document.IssuingCountry,
            IssueDate = document.IssueDate,
            ExpiryDate = document.ExpiryDate,
            Stamp = document.Stamp,
            VehicleRegistration = document.VehicleRegistration,
            SigningDate = document.SigningDate,
            PenaltyPoints = document.PenaltyPoints
        },
        NeedsReview = document.ReviewStatus == DocumentReviewStatus.NeedsReview,
        NewSignatureRequested = document.NewSignatureRequested,
        ReviewReasons = document.ReviewReasons,
        EmployeeNote = document.EmployeeNote,
        UploadedAtUtc = document.UploadedAtUtc,
        Revision = document.Revision,
        Files = files,
        Audit = audit
    };
}
