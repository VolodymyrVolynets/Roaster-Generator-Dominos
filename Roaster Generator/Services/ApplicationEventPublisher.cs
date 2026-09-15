using Microsoft.AspNetCore.SignalR;
using Roaster_Generator.Contracts.Realtime;
using Roaster_Generator.Enums;
using Roaster_Generator.Hubs;
using Roaster_Generator.Security;

namespace Roaster_Generator.Services;

public static class ApplicationEventTypes
{
    public const string AvailabilityChanged = "availabilityChanged";
    public const string DemandChanged = "demandChanged";
    public const string RosterChanged = "rosterChanged";
    public const string SickLeaveChanged = "sickLeaveChanged";
    public const string HolidayChanged = "holidayChanged";
    public const string EmployeeChanged = "employeeChanged";
    public const string RosterSettingsChanged = "rosterSettingsChanged";
}

public sealed class ApplicationEventPublisher(
    IHubContext<ApplicationEventsHub> hub,
    ILogger<ApplicationEventPublisher> logger)
{
    public Task AvailabilityChangedAsync(Guid employeeId, DateOnly weekStart, bool affectsDriverPlanning,
        CancellationToken ct = default) => PublishAsync(
        new ApplicationChangedEvent
        {
            Type = ApplicationEventTypes.AvailabilityChanged,
            EntityId = employeeId,
            WeekStart = weekStart,
            RosterKind = affectsDriverPlanning ? RosterKinds.Drivers : null
        },
        affectsDriverPlanning
            ? [ApplicationEventGroups.Management, ApplicationEventGroups.Employee(employeeId), ApplicationEventGroups.Role(RoleNames.Driver)]
            : [ApplicationEventGroups.Management, ApplicationEventGroups.Employee(employeeId)], ct);

    public Task DemandChangedAsync(Guid entityId, DateOnly weekStart, string demandKind,
        CancellationToken ct = default)
    {
        var rosterKind = demandKind == DemandKinds.Inside ? RosterKinds.Inside : RosterKinds.Drivers;
        var groups = rosterKind == RosterKinds.Drivers
            ? new[] { ApplicationEventGroups.Management, ApplicationEventGroups.Role(RoleNames.Driver) }
            : [ApplicationEventGroups.Management, ApplicationEventGroups.Role(RoleNames.InStore)];
        return PublishAsync(new ApplicationChangedEvent
        {
            Type = ApplicationEventTypes.DemandChanged,
            EntityId = entityId,
            WeekStart = weekStart,
            RosterKind = rosterKind
        }, groups, ct);
    }

    public Task RosterChangedAsync(Guid entityId, DateOnly weekStart, string rosterKind,
        CancellationToken ct = default)
    {
        var employeeRole = rosterKind == RosterKinds.Inside ? RoleNames.InStore : RoleNames.Driver;
        return PublishAsync(new ApplicationChangedEvent
        {
            Type = ApplicationEventTypes.RosterChanged,
            EntityId = entityId,
            WeekStart = weekStart,
            RosterKind = rosterKind
        }, [ApplicationEventGroups.Management, ApplicationEventGroups.Role(employeeRole)], ct);
    }

    public Task SickLeaveChangedAsync(Guid requestId, Guid employeeId, bool affectsDriverPlanning,
        CancellationToken ct = default) => PublishAsync(
        new ApplicationChangedEvent
        {
            Type = ApplicationEventTypes.SickLeaveChanged,
            EntityId = requestId,
            RosterKind = affectsDriverPlanning ? RosterKinds.Drivers : null
        },
        affectsDriverPlanning
            ? [ApplicationEventGroups.Management, ApplicationEventGroups.Employee(employeeId), ApplicationEventGroups.Role(RoleNames.Driver)]
            : [ApplicationEventGroups.Management, ApplicationEventGroups.Employee(employeeId)], ct);

    public Task HolidayChangedAsync(Guid? requestId, IEnumerable<Guid> employeeIds,
        CancellationToken ct = default) => PublishAsync(
        new ApplicationChangedEvent
        {
            Type = ApplicationEventTypes.HolidayChanged,
            EntityId = requestId
        },
        new[] { ApplicationEventGroups.Management }
            .Concat(employeeIds.Distinct().Select(ApplicationEventGroups.Employee)), ct);

    public Task EmployeeChangedAsync(Guid employeeId, CancellationToken ct = default) => PublishAsync(
        new ApplicationChangedEvent
        {
            Type = ApplicationEventTypes.EmployeeChanged,
            EntityId = employeeId
        },
        [ApplicationEventGroups.Management, ApplicationEventGroups.Employee(employeeId),
            ApplicationEventGroups.Role(RoleNames.Driver), ApplicationEventGroups.Role(RoleNames.InStore)], ct);

    public Task RosterSettingsChangedAsync(CancellationToken ct = default) => PublishAsync(
        new ApplicationChangedEvent { Type = ApplicationEventTypes.RosterSettingsChanged },
        [ApplicationEventGroups.Management], ct);

    private async Task PublishAsync(ApplicationChangedEvent payload, IEnumerable<string> groups,
        CancellationToken ct)
    {
        try
        {
            await hub.Clients.Groups(groups.Distinct(StringComparer.Ordinal).ToArray())
                .SendAsync("applicationChanged", payload, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The business change is already committed; a disconnected caller must not turn it into an API failure.
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Real-time delivery failed for {EventType}; clients will recover on reconnect or reload.", payload.Type);
        }
    }
}
