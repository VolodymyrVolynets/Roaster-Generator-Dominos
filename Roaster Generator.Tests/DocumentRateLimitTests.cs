using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Roaster_Generator.Configuration;
using Roaster_Generator.Security;

namespace Roaster_Generator.Tests;

public sealed class DocumentRateLimitTests
{
    [Fact]
    public void Admin_is_not_limited()
    {
        var limiter = LimiterFor(RoleNames.Admin);

        for (var i = 0; i < 50; i++)
            Assert.True(limiter.AttemptAcquire().IsAcquired);
    }

    [Theory]
    [InlineData(RoleNames.Manager)]
    [InlineData(RoleNames.Driver)]
    public void Other_roles_are_limited_to_twenty_an_hour(string role)
    {
        var limiter = LimiterFor(role);

        for (var i = 0; i < 20; i++)
            Assert.True(limiter.AttemptAcquire().IsAcquired);
        Assert.False(limiter.AttemptAcquire().IsAcquired);
    }

    private static System.Threading.RateLimiting.RateLimiter LimiterFor(string role)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim(ClaimTypes.Role, role)],
                "Test"))
        };
        var partition = DocumentRateLimits.Partition(context);
        return partition.Factory(partition.PartitionKey);
    }
}
