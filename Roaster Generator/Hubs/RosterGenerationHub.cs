using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Roaster_Generator.Security;

namespace Roaster_Generator.Hubs;

[Authorize(Roles = RoleNames.Admin)]
public sealed class RosterGenerationHub : Hub;
