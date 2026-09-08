using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Roaster_Generator.Security;
using Roaster_Generator.Services;

namespace Roaster_Generator.Hubs;

[Authorize(Policy = AuthorizationPolicies.Manager)]
public sealed class RosterTimerHub(RosterTimerService rosterTimer) : Hub
{
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
        await rosterTimer.SendActiveLogsAsync(Context.ConnectionId);
    }
}
