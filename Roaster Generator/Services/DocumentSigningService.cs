using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Roaster_Generator.Contracts.Documents;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Enums;

namespace Roaster_Generator.Services;

/// <summary>Administrator setup of signed forms, and employees signing them.</summary>
public sealed class DocumentSigningService(
    AppDbContext db,
    DocumentFileProtector protector,
    DocumentVehicleService vehicles,
    StoreSettingsService storeSettings,
    TimeProvider timeProvider,
    ApplicationEventPublisher? events = null)
{
    private const int MaxSignatureBytes = 1024 * 1024;
    private const string DateFormat = "dd/MM/yyyy";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public async Task<DocumentTemplateResponse?> GetTemplateAsync(Guid typeId, CancellationToken ct)
    {
        var template = await db.DocumentTemplates.AsNoTracking().SingleOrDefaultAsync(item => item.DocumentTypeId == typeId, ct);
        return template is null ? null : ToResponse(template);
    }

    /// <exception cref="InvalidDataException">The file is not a usable fillable PDF.</exception>
    /// <returns>The template, or null when the type does not exist.</returns>
    public async Task<DocumentTemplateResponse?> SaveTemplateAsync(Guid typeId, byte[] content, string fileName, CancellationToken ct)
    {
        if (!await db.DocumentTypes.AnyAsync(type => type.Id == typeId, ct)) return null;

        var fields = DocumentTemplatePdf.ReadFields(content);
        var template = await db.DocumentTemplates.SingleOrDefaultAsync(item => item.DocumentTypeId == typeId, ct);
        if (template is null)
        {
            template = new DocumentTemplate { DocumentTypeId = typeId };
            db.DocumentTemplates.Add(template);
        }

        template.Content = content;
        template.FileName = fileName;
        template.Fields = JsonSerializer.Serialize(fields, Json);
        template.UpdatedAtUtc = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return ToResponse(template);
    }

    /// <exception cref="DocumentSettingsException">The fields don't match the form, or no signature field is chosen.</exception>
    /// <returns>The template, or null when there is none.</returns>
    public async Task<DocumentTemplateResponse?> SaveFieldsAsync(Guid typeId, IReadOnlyList<DocumentTemplateField> fields, CancellationToken ct)
    {
        var template = await db.DocumentTemplates.SingleOrDefaultAsync(item => item.DocumentTypeId == typeId, ct);
        if (template is null) return null;

        var known = Deserialize(template.Fields).Select(field => field.Name).ToHashSet(StringComparer.Ordinal);
        if (!known.SetEquals(fields.Select(field => field.Name)))
            throw new DocumentSettingsException("These fields don't match the form. Reload and try again.");
        if (fields.Count(field => field.Source == DocumentFieldSource.Signature) != 1)
            throw new DocumentSettingsException("Choose exactly one field for the signature.");
        if (fields.Count(field => field.Source == DocumentFieldSource.ManagerSignature) > 1)
            throw new DocumentSettingsException("Choose only one field for the manager's signature.");

        template.Fields = JsonSerializer.Serialize(fields, Json);
        template.UpdatedAtUtc = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return ToResponse(template);
    }

    public async Task<(byte[] Content, string FileName)?> ReadBlankFormAsync(Guid typeId, CancellationToken ct)
    {
        var template = await db.DocumentTemplates.AsNoTracking()
            .SingleOrDefaultAsync(item => item.DocumentTypeId == typeId && item.DocumentType.Kind == DocumentKind.Sign, ct);
        return template is null ? null : (template.Content, template.FileName);
    }

    /// <returns>The form with what is already known filled in, or null when this type can't be signed.</returns>
    public async Task<DocumentSignForm?> GetFormAsync(Employee employee, Guid typeId, CancellationToken ct)
    {
        var type = await SignableTypeAsync(typeId, ct);
        var template = await db.DocumentTemplates.AsNoTracking().SingleOrDefaultAsync(item => item.DocumentTypeId == typeId, ct);
        if (type is null || template is null || !IsReady(template.Fields)) return null;

        var fields = Deserialize(template.Fields);
        var prefill = await PrefillAsync(employee, ct);
        return new DocumentSignForm
        {
            DocumentTypeId = type.Id,
            DocumentTypeName = type.Name,
            Fields = fields
                .Where(IsEmployeeField)
                .Select(field => new DocumentSignField
                {
                    Name = field.Name,
                    Label = field.Label,
                    IsCheckbox = field.IsCheckbox,
                    Required = field.Required,
                    ReadOnly = IsFixed(field.Source, prefill),
                    Value = prefill.GetValueOrDefault(field.Source)
                })
                .ToArray()
        };
    }

    /// <exception cref="DocumentSettingsException">A required value or the signature is missing.</exception>
    /// <returns>The saved document's id, or null when this type can't be signed.</returns>
    public async Task<Guid?> SignAsync(
        Employee employee, DocumentActor actor, Guid typeId, DocumentSignRequest request, CancellationToken ct)
    {
        var type = await SignableTypeAsync(typeId, ct);
        var template = await db.DocumentTemplates.AsNoTracking().SingleOrDefaultAsync(item => item.DocumentTypeId == typeId, ct);
        if (type is null || template is null || !IsReady(template.Fields)) return null;
        if (!protector.IsConfigured) throw new DocumentUnavailableException("Document uploads are not available yet.");

        var signature = ReadSignature(request.Signature);
        var mapping = Deserialize(template.Fields);
        var prefill = await PrefillAsync(employee, ct);
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var field in mapping.Where(IsEmployeeField))
        {
            // Fixed values come from the server, so a form can't be backdated or altered.
            var value = IsFixed(field.Source, prefill)
                ? prefill[field.Source]
                : request.Values.GetValueOrDefault(field.Name)?.Trim();
            if (field.Required && field.IsCheckbox && value != "true")
                throw new DocumentSettingsException($"Please confirm: {field.Label}.");
            if (field.Required && string.IsNullOrEmpty(value))
                throw new DocumentSettingsException($"Please fill in {field.Label}.");
            values[field.Name] = value;
        }

        var isMoped = await db.DriverProfiles.AsNoTracking()
            .AnyAsync(item => item.EmployeeId == employee.Id && item.DriverType == DriverType.Moped, ct);
        foreach (var field in mapping)
        {
            if (field.Source == DocumentFieldSource.AlwaysTicked) values[field.Name] = "true";
            else if (field.Source == DocumentFieldSource.TickedForMoped && isMoped) values[field.Name] = "true";
        }

        var points = ReadPoints(mapping, values);
        var registration = mapping.FirstOrDefault(item => item.Source == DocumentFieldSource.VehicleRegistration) is { } registrationField
            ? values.GetValueOrDefault(registrationField.Name)
            : null;
        var vehicle = await vehicles.GetAsync(employee.Id, ct);
        if (DocumentChecks.IsVehicleDocument(type.Code) && string.IsNullOrWhiteSpace(registration))
            registration = vehicle.Registration;

        byte[] pdf;
        try
        {
            pdf = DocumentTemplatePdf.Fill(template.Content, mapping, values, signature);
        }
        catch (InvalidDataException exception)
        {
            throw new DocumentSettingsException(exception.Message);
        }

        var now = timeProvider.GetUtcNow();
        var today = DocumentDates.Today(timeProvider);
        var needsManager = mapping.Any(item => item.Source == DocumentFieldSource.ManagerSignature);
        var automatic = needsManager ? await AutomaticSignatureAsync(employee.Id, ct) : null;
        if (automatic is not null)
            pdf = CompleteManagerPart(pdf, mapping, automatic.ManagerName, automatic.Png);
        var existing = await db.EmployeeDocuments.Include(item => item.Files)
            .SingleOrDefaultAsync(item => item.EmployeeId == employee.Id && item.DocumentTypeId == type.Id, ct);
        if (existing is not null)
        {
            db.EmployeeDocuments.Remove(existing);
            db.DocumentAuditEvents.Add(Audit(employee.Id, existing.Id, actor, DocumentAuditAction.Replaced, type.Code, now));
        }

        var document = new EmployeeDocument
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            DocumentTypeId = type.Id,
            HolderName = $"{employee.FirstName} {employee.LastName}".Trim(),
            VehicleRegistration = registration,
            SigningDate = today,
            IssueDate = today,
            PenaltyPoints = points,
            ExpiryDate = DocumentChecks.ResolveExpiry(type.ExpiryRule, null, today, points),
            AwaitingManagerSignature = needsManager && automatic is null,
            ManagerSignedByName = automatic?.ManagerName,
            ManagerSignedAtUtc = automatic is null ? null : now,
            UploadedAtUtc = now,
            UploadedByUserId = actor.UserId,
            UpdatedAtUtc = now
        };
        db.EmployeeDocuments.Add(document);
        var fileId = Guid.NewGuid();
        var encrypted = protector.Protect(fileId, pdf);
        db.EmployeeDocumentFiles.Add(new EmployeeDocumentFile
        {
            Id = fileId,
            DocumentId = document.Id,
            FileName = $"{type.Code}.pdf",
            ContentType = "application/pdf",
            Size = pdf.Length,
            EncryptedContent = encrypted.Ciphertext,
            Nonce = encrypted.Nonce,
            Tag = encrypted.Tag,
            KeyId = encrypted.KeyId
        });
        db.DocumentAuditEvents.Add(Audit(employee.Id, document.Id, actor, DocumentAuditAction.Signed, type.Code, now));
        if (automatic is not null)
            db.DocumentAuditEvents.Add(Audit(employee.Id, document.Id,
                new DocumentActor(null, $"{automatic.ManagerName} (automatic)", false), DocumentAuditAction.ManagerSigned, type.Code, now));
        await db.SaveChangesAsync(ct);

        if (DocumentChecks.IsVehicleDocument(type.Code))
            await vehicles.DocumentSavedAsync(employee.Id, actor, registration, ct);
        if (events is not null) await events.DocumentsChangedAsync(employee.Id, ct);
        return document.Id;
    }

    public async Task<IReadOnlyList<DocumentSignOffSummary>> ListSignOffsAsync(CancellationToken ct)
    {
        var documents = await db.EmployeeDocuments.AsNoTracking()
            .Include(item => item.Employee).Include(item => item.DocumentType)
            .Where(item => item.AwaitingManagerSignature && !item.Employee.IsDeleted)
            .ToListAsync(ct);
        return documents.OrderBy(item => item.UploadedAtUtc).Select(item => new DocumentSignOffSummary
        {
            DocumentId = item.Id,
            EmployeeName = FullName(item.Employee),
            DocumentTypeName = item.DocumentType.Name,
            SignedAtUtc = item.UploadedAtUtc
        }).ToArray();
    }

    /// <returns>The sign-off, or null when this document isn't waiting for a manager.</returns>
    public async Task<DocumentSignOffForm?> GetSignOffAsync(Guid documentId, DocumentSignOffActor manager, CancellationToken ct)
    {
        var document = await PendingAsync(documentId, ct);
        if (document is null) return null;

        var mapping = await MappingAsync(document.DocumentTypeId, ct);
        return new DocumentSignOffForm
        {
            DocumentId = document.Id,
            EmployeeName = FullName(document.Employee),
            DocumentTypeName = document.DocumentType.Name,
            ManagerName = manager.DisplayName,
            AsksForManagerName = mapping.Any(item => item.Source == DocumentFieldSource.ManagerName)
        };
    }

    /// <summary>Lets a manager read a form before signing it. Only forms waiting for a signature are available.</summary>
    public async Task<byte[]?> ReadSignOffPdfAsync(Guid documentId, DocumentSignOffActor manager, CancellationToken ct)
    {
        var document = await PendingAsync(documentId, ct);
        if (document is null) return null;

        var file = await db.EmployeeDocumentFiles.AsNoTracking().SingleAsync(item => item.DocumentId == documentId, ct);
        var content = protector.Unprotect(file.Id, new ProtectedDocumentFile(file.EncryptedContent, file.Nonce, file.Tag, file.KeyId));
        db.DocumentAuditEvents.Add(Audit(document.EmployeeId, documentId, manager.Actor, DocumentAuditAction.Viewed,
            document.DocumentType.Code, timeProvider.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        return content;
    }

    /// <exception cref="DocumentSettingsException">The manager is signing their own form, or the signature is missing.</exception>
    /// <returns>False when this document isn't waiting for a manager.</returns>
    public async Task<bool> ManagerSignAsync(
        Guid documentId, DocumentSignOffActor manager, DocumentSignOffRequest request, CancellationToken ct)
    {
        var document = await db.EmployeeDocuments
            .Include(item => item.Employee).Include(item => item.DocumentType)
            .SingleOrDefaultAsync(item => item.Id == documentId && item.AwaitingManagerSignature, ct);
        if (document is null) return false;
        if (document.EmployeeId == manager.EmployeeId)
            throw new DocumentSettingsException("Another manager has to sign your own form.");

        var signature = ReadSignature(request.Signature);
        var name = string.IsNullOrWhiteSpace(request.ManagerName) ? manager.DisplayName : request.ManagerName.Trim();
        var mapping = await MappingAsync(document.DocumentTypeId, ct);
        var file = await db.EmployeeDocumentFiles.SingleAsync(item => item.DocumentId == documentId, ct);
        var signedForm = protector.Unprotect(file.Id, new ProtectedDocumentFile(file.EncryptedContent, file.Nonce, file.Tag, file.KeyId));
        var completed = CompleteManagerPart(signedForm, mapping, name, signature);

        var encrypted = protector.Protect(file.Id, completed);
        file.EncryptedContent = encrypted.Ciphertext;
        file.Nonce = encrypted.Nonce;
        file.Tag = encrypted.Tag;
        file.KeyId = encrypted.KeyId;
        file.Size = completed.Length;
        var now = timeProvider.GetUtcNow();
        document.AwaitingManagerSignature = false;
        document.ManagerSignedByName = name;
        document.ManagerSignedAtUtc = now;
        document.UpdatedAtUtc = now;
        db.DocumentAuditEvents.Add(Audit(document.EmployeeId, document.Id, manager.Actor, DocumentAuditAction.ManagerSigned, document.DocumentType.Code, now));
        await db.SaveChangesAsync(ct);
        if (events is not null) await events.DocumentsChangedAsync(document.EmployeeId, ct);
        return true;
    }

    public async Task<DocumentAutoSignatureResponse?> GetAutomaticSignatureAsync(CancellationToken ct)
    {
        var saved = await db.DocumentManagerSignatures.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == DocumentManagerSignature.SingletonId, ct);
        return saved is null ? null : new DocumentAutoSignatureResponse { ManagerName = saved.ManagerName, SavedAtUtc = saved.SavedAtUtc };
    }

    /// <summary>Saves the signature placed automatically from now on. Forms already waiting stay in the list.</summary>
    /// <exception cref="DocumentSettingsException">The signature is missing.</exception>
    public async Task<DocumentAutoSignatureResponse> SaveAutomaticSignatureAsync(
        DocumentSignOffActor manager, DocumentSignOffRequest request, CancellationToken ct)
    {
        if (!protector.IsConfigured) throw new DocumentUnavailableException("Document uploads are not available yet.");
        var png = ReadSignature(request.Signature);
        var encrypted = protector.Protect(DocumentManagerSignature.SingletonId, png);
        var saved = await db.DocumentManagerSignatures.SingleOrDefaultAsync(item => item.Id == DocumentManagerSignature.SingletonId, ct);
        if (saved is null)
        {
            saved = new DocumentManagerSignature();
            db.DocumentManagerSignatures.Add(saved);
        }

        saved.ManagerName = string.IsNullOrWhiteSpace(request.ManagerName) ? manager.DisplayName : request.ManagerName.Trim();
        saved.ManagerEmployeeId = manager.EmployeeId;
        saved.EncryptedContent = encrypted.Ciphertext;
        saved.Nonce = encrypted.Nonce;
        saved.Tag = encrypted.Tag;
        saved.KeyId = encrypted.KeyId;
        saved.SavedAtUtc = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return new DocumentAutoSignatureResponse { ManagerName = saved.ManagerName, SavedAtUtc = saved.SavedAtUtc };
    }

    public async Task<bool> RemoveAutomaticSignatureAsync(CancellationToken ct)
    {
        var saved = await db.DocumentManagerSignatures.SingleOrDefaultAsync(item => item.Id == DocumentManagerSignature.SingletonId, ct);
        if (saved is null) return false;
        db.DocumentManagerSignatures.Remove(saved);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private sealed record AutomaticSignature(string ManagerName, byte[] Png);

    /// <summary>A manager's saved signature never signs their own form; that one waits for another manager.</summary>
    private async Task<AutomaticSignature?> AutomaticSignatureAsync(Guid employeeId, CancellationToken ct)
    {
        var saved = await db.DocumentManagerSignatures.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == DocumentManagerSignature.SingletonId, ct);
        if (saved is null || saved.ManagerEmployeeId == employeeId) return null;
        return new AutomaticSignature(saved.ManagerName, protector.Unprotect(
            DocumentManagerSignature.SingletonId,
            new ProtectedDocumentFile(saved.EncryptedContent, saved.Nonce, saved.Tag, saved.KeyId)));
    }

    private byte[] CompleteManagerPart(byte[] signedForm, IReadOnlyList<DocumentTemplateField> mapping, string name, byte[] signature)
    {
        var today = DocumentDates.Today(timeProvider).ToString(DateFormat, CultureInfo.InvariantCulture);
        var values = mapping.Where(item => DocumentTemplatePdf.IsManagerSource(item.Source)).ToDictionary(
            item => item.Name,
            item => (string?)(item.Source == DocumentFieldSource.ManagerDate ? today : name),
            StringComparer.Ordinal);
        try
        {
            return DocumentTemplatePdf.FillManagerPart(signedForm, mapping, values, signature);
        }
        catch (InvalidDataException exception)
        {
            throw new DocumentSettingsException(exception.Message);
        }
    }

    private Task<EmployeeDocument?> PendingAsync(Guid documentId, CancellationToken ct) =>
        db.EmployeeDocuments.AsNoTracking().Include(item => item.Employee).Include(item => item.DocumentType)
            .SingleOrDefaultAsync(item => item.Id == documentId && item.AwaitingManagerSignature, ct);

    private async Task<List<DocumentTemplateField>> MappingAsync(Guid typeId, CancellationToken ct)
    {
        var template = await db.DocumentTemplates.AsNoTracking().SingleOrDefaultAsync(item => item.DocumentTypeId == typeId, ct);
        return template is null ? [] : Deserialize(template.Fields);
    }

    private static string FullName(Employee employee) => $"{employee.FirstName} {employee.LastName}".Trim();

    private static bool IsEmployeeField(DocumentTemplateField field) =>
        field.Source is not (DocumentFieldSource.Signature or DocumentFieldSource.Ignore
            or DocumentFieldSource.AlwaysTicked or DocumentFieldSource.TickedForMoped) &&
        !DocumentTemplatePdf.IsManagerSource(field.Source);

    /// <summary>Set by the business, not the employee. A franchisee name not set up yet can be typed in.</summary>
    private static bool IsFixed(DocumentFieldSource source, IReadOnlyDictionary<DocumentFieldSource, string?> prefill) =>
        source == DocumentFieldSource.Today ||
        (source == DocumentFieldSource.FranchiseeName && !string.IsNullOrEmpty(prefill.GetValueOrDefault(source)));

    /// <summary>A form can only be signed once exactly one field holds the signature.</summary>
    public static bool IsReady(string fieldsJson) =>
        Deserialize(fieldsJson).Count(field => field.Source == DocumentFieldSource.Signature) == 1;

    private Task<DocumentType?> SignableTypeAsync(Guid typeId, CancellationToken ct) =>
        db.DocumentTypes.AsNoTracking()
            .SingleOrDefaultAsync(type => type.Id == typeId && type.IsActive && type.Kind == DocumentKind.Sign, ct);

    private async Task<Dictionary<DocumentFieldSource, string?>> PrefillAsync(Employee employee, CancellationToken ct)
    {
        var store = await storeSettings.GetAsync(ct);
        var vehicle = await vehicles.GetAsync(employee.Id, ct);
        return new Dictionary<DocumentFieldSource, string?>
        {
            [DocumentFieldSource.FullName] = $"{employee.FirstName} {employee.LastName}".Trim(),
            [DocumentFieldSource.FirstName] = employee.FirstName,
            [DocumentFieldSource.LastName] = employee.LastName,
            [DocumentFieldSource.EmployeeNumber] = employee.EmployeeNumber,
            [DocumentFieldSource.PhoneNumber] = employee.PhoneNumber,
            [DocumentFieldSource.PayrollNumber] = employee.PayrollNumber,
            [DocumentFieldSource.VehicleRegistration] = vehicle.Registration,
            [DocumentFieldSource.Today] = DocumentDates.Today(timeProvider).ToString(DateFormat, CultureInfo.InvariantCulture),
            [DocumentFieldSource.StoreId] = store.StoreId,
            [DocumentFieldSource.StoreName] = store.StoreName,
            [DocumentFieldSource.FranchiseeName] = store.FranchiseeName
        };
    }

    private static int? ReadPoints(IEnumerable<DocumentTemplateField> mapping, IReadOnlyDictionary<string, string?> values)
    {
        if (mapping.FirstOrDefault(item => item.Source == DocumentFieldSource.PenaltyPoints) is not { } field ||
            string.IsNullOrEmpty(values.GetValueOrDefault(field.Name))) return null;
        return int.TryParse(values[field.Name], NumberStyles.None, CultureInfo.InvariantCulture, out var points) && points <= 100
            ? points
            : throw new DocumentSettingsException($"{field.Label} must be a whole number.");
    }

    private static byte[] ReadSignature(string signature)
    {
        byte[] content;
        try
        {
            content = Convert.FromBase64String(signature);
        }
        catch (FormatException)
        {
            throw new DocumentSettingsException("Please sign the form.");
        }

        if (content.Length is 0 or > MaxSignatureBytes || !DocumentTemplatePdf.IsPng(content))
            throw new DocumentSettingsException("Please sign the form.");
        return content;
    }

    private static List<DocumentTemplateField> Deserialize(string json) =>
        JsonSerializer.Deserialize<List<DocumentTemplateField>>(json, Json) ?? [];

    private static DocumentTemplateResponse ToResponse(DocumentTemplate template) => new()
    {
        FileName = template.FileName,
        Fields = Deserialize(template.Fields)
    };

    private static DocumentAuditEvent Audit(
        Guid employeeId, Guid documentId, DocumentActor actor, DocumentAuditAction action, string code, DateTimeOffset now) => new()
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
