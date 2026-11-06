using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Roaster_Generator.Contracts.Documents;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Enums;

namespace Roaster_Generator.Services;

public sealed record DocumentActor(Guid? UserId, string Name, bool IsAdministrator);

public sealed class DocumentUnavailableException(string message) : Exception(message);

/// <summary>
/// Holds analysed files until the person confirms them. The stored document is always the file
/// that was analysed, and the analysis stays on the server so a client cannot alter it.
/// </summary>
public sealed class DocumentDraftService(
    AppDbContext db,
    IDocumentAnalyser analyser,
    DocumentFileProtector protector,
    DocumentVehicleService vehicles,
    TimeProvider timeProvider,
    ApplicationEventPublisher? events = null)
{
    public static readonly TimeSpan DraftLifetime = TimeSpan.FromHours(1);

    private const string AnalysisUnavailable = "This document could not be checked automatically.";
    private const string Unreadable = "The document could not be read clearly.";
    private const string UnknownType = "The document type could not be identified.";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<DocumentAnalysisResponse> AnalyseAsync(
        Employee employee,
        DocumentActor actor,
        IReadOnlyList<ValidatedDocumentFile> files,
        Guid? requestedTypeId,
        CancellationToken ct)
    {
        if (!protector.IsConfigured)
            throw new DocumentUnavailableException("Document uploads are not available yet.");

        var types = await db.DocumentTypes.AsNoTracking().Where(type => type.IsActive)
            .OrderBy(type => type.SortOrder).ToListAsync(ct);
        var analysis = await analyser.AnalyseAsync(
            files, types.Select(type => new DocumentCatalogueEntry(type.Code, type.Name)).ToArray(), ct);

        var now = timeProvider.GetUtcNow();
        db.DocumentDrafts.RemoveRange(await db.DocumentDrafts
            .Where(draft => draft.EmployeeId == employee.Id && draft.CreatedByUserId == actor.UserId)
            .Include(draft => draft.Files)
            .ToListAsync(ct));
        var draft = new DocumentDraft
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            CreatedByUserId = actor.UserId,
            AnalysisResult = analysis is null ? null : JsonSerializer.Serialize(analysis, Json),
            CreatedAtUtc = now,
            ExpiresAtUtc = now + DraftLifetime
        };
        db.DocumentDrafts.Add(draft);
        db.EmployeeDocumentFiles.AddRange(files.Select((file, index) => ToEntity(file, index, draft.Id)));
        var analysedType = types.FirstOrDefault(type => type.Code == analysis?.DocumentType);
        var requestedType = types.FirstOrDefault(type => type.Id == requestedTypeId);
        db.DocumentAuditEvents.Add(Audit(employee.Id, null, actor, DocumentAuditAction.Analysed,
            analysedType?.Code ?? DocumentAnalysis.UnknownType, now));
        await db.SaveChangesAsync(ct);

        var vehicle = await vehicles.GetAsync(employee.Id, ct);
        var warnings = await WarningsAsync(employee, analysis, analysedType, vehicle, ct);
        var vehiclePrompt = VehiclePrompt(analysis, analysedType, vehicle);
        if (analysedType is not null && requestedType is not null && analysedType.Id != requestedType.Id)
            warnings.Add($"This looks like {analysedType.Name}, not {requestedType.Name}.");

        return new DocumentAnalysisResponse
        {
            DraftId = draft.Id,
            ExpiresAtUtc = draft.ExpiresAtUtc,
            IsAnalysed = analysis is not null,
            DocumentTypeId = analysedType?.Id ?? requestedType?.Id,
            Summary = analysis?.Summary,
            Fields = analysis?.Fields ?? new DocumentFields(),
            Warnings = warnings,
            VehiclePrompt = vehiclePrompt
        };
    }

    /// <returns>The saved document, or null when the draft does not exist, has expired, or is someone else's.</returns>
    public async Task<DocumentSubmitResponse?> SubmitAsync(
        Employee employee, DocumentActor actor, Guid draftId, DocumentSubmitRequest request, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var draft = await db.DocumentDrafts.Include(item => item.Files)
            .SingleOrDefaultAsync(item => item.Id == draftId && item.EmployeeId == employee.Id &&
                                          item.CreatedByUserId == actor.UserId && item.ExpiresAtUtc > now, ct);
        if (draft is null) return null;

        var type = await db.DocumentTypes.SingleOrDefaultAsync(item => item.Id == request.DocumentTypeId && item.IsActive, ct)
            ?? throw new DocumentSettingsException("Choose a document type from the list.");
        var analysis = Deserialize(draft.AnalysisResult);
        var fields = Normalize(request);

        var reasons = actor.IsAdministrator ? [] : await ReviewReasonsAsync(employee, type, fields, analysis, ct);
        var existing = await db.EmployeeDocuments.Include(item => item.Files)
            .SingleOrDefaultAsync(item => item.EmployeeId == employee.Id && item.DocumentTypeId == type.Id, ct);
        var document = new EmployeeDocument
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            DocumentTypeId = type.Id,
            HolderName = fields.HolderName,
            ReferenceNumber = fields.ReferenceNumber,
            IssuingCountry = fields.IssuingCountry,
            IssueDate = fields.IssueDate,
            ExpiryDate = DocumentChecks.ResolveExpiry(type.ExpiryRule, fields.ExpiryDate, fields.SigningDate, fields.PenaltyPoints),
            Stamp = fields.Stamp,
            VehicleRegistration = fields.VehicleRegistration,
            SigningDate = fields.SigningDate,
            PenaltyPoints = fields.PenaltyPoints,
            ReviewStatus = reasons.Count > 0 ? DocumentReviewStatus.NeedsReview : DocumentReviewStatus.Clear,
            ReviewReasons = reasons,
            EmployeeNote = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            UploadedAtUtc = now,
            UploadedByUserId = actor.UserId,
            UpdatedAtUtc = now
        };

        if (existing is not null)
        {
            db.EmployeeDocuments.Remove(existing);
            db.DocumentAuditEvents.Add(Audit(employee.Id, existing.Id, actor, DocumentAuditAction.Replaced, type.Code, now));
        }

        db.EmployeeDocuments.Add(document);
        foreach (var file in draft.Files)
        {
            file.DraftId = null;
            file.DocumentId = document.Id;
        }

        draft.Files.Clear();
        db.DocumentDrafts.Remove(draft);
        db.DocumentAuditEvents.Add(Audit(employee.Id, document.Id, actor, DocumentAuditAction.Uploaded, type.Code, now));
        await db.SaveChangesAsync(ct);

        if (DocumentChecks.IsVehicleDocument(type.Code))
            await vehicles.DocumentSavedAsync(employee.Id, actor, fields.VehicleRegistration, ct);
        if (events is not null) await events.DocumentsChangedAsync(employee.Id, ct);
        return new DocumentSubmitResponse
        {
            DocumentId = document.Id,
            NeedsReview = reasons.Count > 0,
            ReplacedExisting = existing is not null
        };
    }

    /// <returns>False when there is no such draft for this employee and actor.</returns>
    public async Task<bool> DiscardAsync(Employee employee, DocumentActor actor, Guid draftId, CancellationToken ct)
    {
        var draft = await db.DocumentDrafts.Include(item => item.Files)
            .SingleOrDefaultAsync(item => item.Id == draftId && item.EmployeeId == employee.Id &&
                                          item.CreatedByUserId == actor.UserId, ct);
        if (draft is null) return false;
        db.DocumentDrafts.Remove(draft);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<List<string>> ReviewReasonsAsync(
        Employee employee, DocumentType type, DocumentFields fields, DocumentAnalysis? analysis, CancellationToken ct)
    {
        var reasons = new List<string>();
        if (analysis is null)
        {
            reasons.Add(AnalysisUnavailable);
        }
        else
        {
            if (!analysis.Readable) reasons.Add(Unreadable);
            if (analysis.DocumentType == DocumentAnalysis.UnknownType) reasons.Add(UnknownType);
            reasons.AddRange(analysis.Problems);
            var changed = ChangedFields(analysis, type, fields);
            if (changed.Count > 0) reasons.Add($"Changed by the employee: {string.Join(", ", changed)}.");
        }

        var vehicle = await vehicles.GetAsync(employee.Id, ct);
        reasons.AddRange(Check(employee, type, fields, vehicle).Select(warning => warning.Message));
        return reasons.Distinct(StringComparer.Ordinal).ToList();
    }

    private async Task<List<string>> WarningsAsync(
        Employee employee, DocumentAnalysis? analysis, DocumentType? type, EmployeeVehicleState vehicle, CancellationToken ct)
    {
        if (analysis is null) return [AnalysisUnavailable];

        var warnings = new List<string>();
        if (!analysis.Readable) warnings.Add(Unreadable);
        if (analysis.DocumentType == DocumentAnalysis.UnknownType) warnings.Add(UnknownType);
        warnings.AddRange(analysis.Problems);
        if (type is not null)
            warnings.AddRange(Check(employee, type, analysis.Fields, vehicle)
                .Where(warning => warning.Code != DocumentWarningCode.RegistrationMismatch || vehicle.OpenChange is not null)
                .Select(warning => warning.Message));
        return warnings.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>With no change open, a different registration is asked about rather than warned about.</summary>
    private static DocumentVehiclePrompt? VehiclePrompt(DocumentAnalysis? analysis, DocumentType? type, EmployeeVehicleState vehicle)
    {
        if (type is null || !DocumentChecks.IsVehicleDocument(type.Code) || !vehicle.TracksVehicle ||
            vehicle.OpenChange is not null || vehicle.Registration is null ||
            analysis?.Fields.VehicleRegistration is not { } detected ||
            DocumentChecks.NormalizeRegistration(detected) == DocumentChecks.NormalizeRegistration(vehicle.Registration))
            return null;

        return new DocumentVehiclePrompt { CurrentRegistration = vehicle.Registration, DetectedRegistration = detected };
    }

    private IReadOnlyList<DocumentWarning> Check(
        Employee employee, DocumentType type, DocumentFields fields, EmployeeVehicleState vehicle) =>
        DocumentChecks.Evaluate(
            new DocumentFacts(type.Code, type.Name, type.ExpiryRule, fields.HolderName, fields.IssuingCountry,
                fields.ExpiryDate, fields.SigningDate, fields.PenaltyPoints, fields.VehicleRegistration),
            employee.FirstName,
            employee.LastName,
            vehicle.TracksVehicle && vehicle.Registration is not null ? [vehicle.Registration] : [],
            DocumentDates.Today(timeProvider));

    private static List<string> ChangedFields(DocumentAnalysis analysis, DocumentType type, DocumentFields fields)
    {
        var read = analysis.Fields;
        var changed = new List<string>();
        void Compare(string name, string? original, string? current)
        {
            if (!string.Equals(original?.Trim(), current, StringComparison.OrdinalIgnoreCase)) changed.Add(name);
        }

        if (analysis.DocumentType != type.Code) changed.Add("document type");
        Compare("name", read.HolderName, fields.HolderName);
        Compare("reference number", read.ReferenceNumber, fields.ReferenceNumber);
        Compare("issuing country", read.IssuingCountry, fields.IssuingCountry);
        Compare("stamp", read.Stamp, fields.Stamp);
        Compare("vehicle registration", read.VehicleRegistration, fields.VehicleRegistration);
        if (read.IssueDate != fields.IssueDate) changed.Add("issue date");
        if (read.ExpiryDate != fields.ExpiryDate) changed.Add("expiry date");
        if (read.SigningDate != fields.SigningDate) changed.Add("signing date");
        if (read.PenaltyPoints != fields.PenaltyPoints) changed.Add("penalty points");
        return changed;
    }

    internal static DocumentFields Normalize(DocumentFields fields) => new()
    {
        HolderName = Clean(fields.HolderName),
        ReferenceNumber = Clean(fields.ReferenceNumber),
        IssuingCountry = DocumentCountries.Normalize(fields.IssuingCountry),
        IssueDate = fields.IssueDate,
        ExpiryDate = fields.ExpiryDate,
        Stamp = Clean(fields.Stamp),
        VehicleRegistration = Clean(fields.VehicleRegistration),
        SigningDate = fields.SigningDate,
        PenaltyPoints = fields.PenaltyPoints
    };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DocumentAnalysis? Deserialize(string? json) =>
        json is null ? null : JsonSerializer.Deserialize<DocumentAnalysis>(json, Json);

    private EmployeeDocumentFile ToEntity(ValidatedDocumentFile file, int index, Guid draftId)
    {
        var id = Guid.NewGuid();
        var encrypted = protector.Protect(id, file.Content);
        return new EmployeeDocumentFile
        {
            Id = id,
            DraftId = draftId,
            PageIndex = index,
            FileName = file.FileName,
            ContentType = file.ContentType,
            Size = file.Content.Length,
            EncryptedContent = encrypted.Ciphertext,
            Nonce = encrypted.Nonce,
            Tag = encrypted.Tag,
            KeyId = encrypted.KeyId
        };
    }

    private static DocumentAuditEvent Audit(
        Guid employeeId, Guid? documentId, DocumentActor actor, DocumentAuditAction action, string code, DateTimeOffset now) => new()
    {
        EmployeeId = employeeId,
        DocumentId = documentId,
        ActorUserId = actor.UserId,
        ActorName = actor.Name,
        Action = action,
        DocumentTypeCode = code,
        OccurredAtUtc = now
    };
}

public static class DocumentDates
{
    private static readonly TimeZoneInfo ShopTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Dublin");

    public static DateOnly Today(TimeProvider timeProvider) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), ShopTimeZone).DateTime);
}
