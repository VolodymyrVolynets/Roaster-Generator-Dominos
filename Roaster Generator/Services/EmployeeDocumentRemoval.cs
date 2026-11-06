using Microsoft.EntityFrameworkCore;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Enums;

namespace Roaster_Generator.Services;

/// <summary>
/// Permanently removes everything uploaded by or for an employee, because identity documents
/// are not kept after removal. Changes are staged for the caller's transaction to save.
/// </summary>
public static class EmployeeDocumentRemoval
{
    public static async Task StageAsync(
        AppDbContext db,
        Guid employeeId,
        Guid? actorUserId,
        string actorName,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var documents = await db.EmployeeDocuments
            .Include(document => document.DocumentType)
            .Include(document => document.Files)
            .Where(document => document.EmployeeId == employeeId)
            .ToListAsync(cancellationToken);
        var drafts = await db.DocumentDrafts
            .Include(draft => draft.Files)
            .Where(draft => draft.EmployeeId == employeeId)
            .ToListAsync(cancellationToken);

        db.DocumentAuditEvents.AddRange(documents.Select(document => new DocumentAuditEvent
        {
            EmployeeId = employeeId,
            DocumentId = document.Id,
            ActorUserId = actorUserId,
            ActorName = actorName,
            Action = DocumentAuditAction.Deleted,
            DocumentTypeCode = document.DocumentType.Code,
            OccurredAtUtc = now
        }));
        db.EmployeeDocuments.RemoveRange(documents);
        db.DocumentDrafts.RemoveRange(drafts);
    }
}
