using Microsoft.EntityFrameworkCore;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Enums;

namespace Roaster_Generator.Services;

public sealed record EmployeeVehicleState(bool TracksVehicle, string? Registration, VehicleChange? OpenChange)
{
    public DocumentVehicleContext? Context =>
        Registration is null ? null : new DocumentVehicleContext(Registration, OpenChange is not null);
}

/// <summary>
/// Tracks the employee's current vehicle and car changes. Starting a change needs no approval:
/// it only changes what the documents are compared with, and never affects rostering.
/// </summary>
public sealed class DocumentVehicleService(
    AppDbContext db,
    TimeProvider timeProvider,
    ApplicationEventPublisher? events = null)
{
    private const string AuditCode = "vehicle";

    public async Task<EmployeeVehicleState> GetAsync(Guid employeeId, CancellationToken ct)
    {
        var categories = await CategoriesAsync(employeeId, ct);
        var vehicle = await db.EmployeeVehicles.AsNoTracking().SingleOrDefaultAsync(item => item.EmployeeId == employeeId, ct);
        var change = await OpenChangeAsync(employeeId, ct);
        return new EmployeeVehicleState(DocumentRequirementResolver.TracksVehicle(categories), vehicle?.Registration, change);
    }

    /// <summary>Starts a car change, or corrects the registration of the one already open.</summary>
    /// <exception cref="DocumentSettingsException">The employee has no tracked vehicle, or this is already the current one.</exception>
    public async Task StartChangeAsync(Employee employee, DocumentActor actor, string registration, CancellationToken ct)
    {
        var state = await GetAsync(employee.Id, ct);
        if (!state.TracksVehicle)
            throw new DocumentSettingsException("Car changes only apply to an own car or moped.");

        var next = registration.Trim().ToUpperInvariant();
        if (state.Registration is not null &&
            DocumentChecks.NormalizeRegistration(state.Registration) == DocumentChecks.NormalizeRegistration(next))
            throw new DocumentSettingsException("This is already the current vehicle.");

        var now = timeProvider.GetUtcNow();
        var vehicle = await db.EmployeeVehicles.SingleOrDefaultAsync(item => item.EmployeeId == employee.Id, ct);
        if (vehicle is null)
        {
            db.EmployeeVehicles.Add(new EmployeeVehicle { EmployeeId = employee.Id, Registration = next, UpdatedAtUtc = now });
        }
        else
        {
            if (state.OpenChange is not null)
            {
                var open = await db.VehicleChanges.SingleAsync(item => item.Id == state.OpenChange.Id, ct);
                open.NewRegistration = next;
            }
            else
            {
                db.VehicleChanges.Add(new VehicleChange
                {
                    EmployeeId = employee.Id,
                    OldRegistration = vehicle.Registration,
                    NewRegistration = next,
                    StartedAtUtc = now,
                    StartedByUserId = actor.UserId,
                    StartedByName = actor.Name
                });
            }

            vehicle.Registration = next;
            vehicle.UpdatedAtUtc = now;
        }

        db.DocumentAuditEvents.Add(Audit(employee.Id, actor, DocumentAuditAction.VehicleChanged));
        await db.SaveChangesAsync(ct);
        await CompleteIfDoneAsync(employee.Id, actor, ct);
        await PublishAsync(employee.Id, ct);
    }

    /// <summary>Restores the previous registration.</summary>
    /// <returns>False when no change is open.</returns>
    public async Task<bool> CancelChangeAsync(Employee employee, DocumentActor actor, CancellationToken ct)
    {
        var change = await db.VehicleChanges.SingleOrDefaultAsync(
            item => item.EmployeeId == employee.Id && item.Status == VehicleChangeStatus.Open, ct);
        if (change is null) return false;

        var now = timeProvider.GetUtcNow();
        var vehicle = await db.EmployeeVehicles.SingleAsync(item => item.EmployeeId == employee.Id, ct);
        vehicle.Registration = change.OldRegistration;
        vehicle.UpdatedAtUtc = now;
        change.Status = VehicleChangeStatus.Cancelled;
        change.ClosedAtUtc = now;
        db.DocumentAuditEvents.Add(Audit(employee.Id, actor, DocumentAuditAction.VehicleCancelled));
        await db.SaveChangesAsync(ct);
        await PublishAsync(employee.Id, ct);
        return true;
    }

    /// <summary>
    /// Call after a vehicle document is saved. The first registration on file becomes the
    /// current vehicle, and an open change closes once every vehicle document matches.
    /// </summary>
    public async Task DocumentSavedAsync(Guid employeeId, DocumentActor actor, string? registration, CancellationToken ct)
    {
        var state = await GetAsync(employeeId, ct);
        if (!state.TracksVehicle) return;

        if (state.Registration is null && !string.IsNullOrWhiteSpace(registration))
        {
            db.EmployeeVehicles.Add(new EmployeeVehicle
            {
                EmployeeId = employeeId,
                Registration = registration.Trim().ToUpperInvariant(),
                UpdatedAtUtc = timeProvider.GetUtcNow()
            });
            await db.SaveChangesAsync(ct);
        }

        await CompleteIfDoneAsync(employeeId, actor, ct);
    }

    private async Task CompleteIfDoneAsync(Guid employeeId, DocumentActor actor, CancellationToken ct)
    {
        var change = await db.VehicleChanges.SingleOrDefaultAsync(
            item => item.EmployeeId == employeeId && item.Status == VehicleChangeStatus.Open, ct);
        if (change is null) return;

        var categories = await CategoriesAsync(employeeId, ct);
        var requirements = await db.DocumentRequirements.AsNoTracking().Include(item => item.DocumentType).ToListAsync(ct);
        var types = await db.DocumentTypes.AsNoTracking().ToListAsync(ct);
        var documents = await db.EmployeeDocuments.AsNoTracking().Include(item => item.DocumentType)
            .Where(item => item.EmployeeId == employeeId).ToListAsync(ct);
        var rows = DocumentRequirementResolver.Resolve(categories, requirements, types, documents,
            DocumentDates.Today(timeProvider), new DocumentVehicleContext(change.NewRegistration, true));
        if (DocumentRequirementResolver.VehicleDocumentsOutstanding(rows) > 0) return;

        change.Status = VehicleChangeStatus.Completed;
        change.ClosedAtUtc = timeProvider.GetUtcNow();
        db.DocumentAuditEvents.Add(Audit(employeeId, actor, DocumentAuditAction.VehicleCompleted));
        await db.SaveChangesAsync(ct);
    }

    private Task<VehicleChange?> OpenChangeAsync(Guid employeeId, CancellationToken ct) =>
        db.VehicleChanges.AsNoTracking()
            .SingleOrDefaultAsync(item => item.EmployeeId == employeeId && item.Status == VehicleChangeStatus.Open, ct);

    private async Task<IReadOnlySet<DocumentCategory>> CategoriesAsync(Guid employeeId, CancellationToken ct)
    {
        var employee = await db.Employees.AsNoTracking()
            .Include(item => item.DriverProfile).Include(item => item.InStoreProfile).Include(item => item.ManagerProfile)
            .SingleAsync(item => item.Id == employeeId, ct);
        var roles = await (
            from user in db.Users
            where user.EmployeeId == employeeId
            join userRole in db.UserRoles on user.Id equals userRole.UserId
            join role in db.Roles on userRole.RoleId equals role.Id
            select role.Name).ToListAsync(ct);
        return DocumentRequirementResolver.CategoriesFor(employee, roles.Where(role => role is not null)!);
    }

    private DocumentAuditEvent Audit(Guid employeeId, DocumentActor actor, DocumentAuditAction action) => new()
    {
        EmployeeId = employeeId,
        ActorUserId = actor.UserId,
        ActorName = actor.Name,
        Action = action,
        DocumentTypeCode = AuditCode,
        OccurredAtUtc = timeProvider.GetUtcNow()
    };

    private async Task PublishAsync(Guid employeeId, CancellationToken ct)
    {
        if (events is not null) await events.DocumentsChangedAsync(employeeId, ct);
    }
}
