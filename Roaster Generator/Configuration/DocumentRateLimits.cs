using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Roaster_Generator.Configuration;

/// <summary>Each analysis is a paid API call, so uploads are limited per signed-in user.</summary>
public static class DocumentRateLimits
{
    public const string AnalysisPolicy = "document-analysis";

    private const int AnalysesPerHour = 20;

    public static void Configure(RateLimiterOptions options)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = (context, ct) => new ValueTask(context.HttpContext.Response.WriteAsJsonAsync(
            new { message = "Too many uploads. Please wait a while and try again." }, ct));
        options.AddPolicy(AnalysisPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = AnalysesPerHour, Window = TimeSpan.FromHours(1) }));
    }
}
