using Microsoft.EntityFrameworkCore;
using Roaster_Generator.Data;

namespace Roaster_Generator.Services;

/// <summary>Permanently removes drafts nobody confirmed, so unconfirmed identity documents are not kept.</summary>
public sealed class DocumentDraftCleanupService(
    IServiceScopeFactory scopes,
    TimeProvider timeProvider,
    ILogger<DocumentDraftCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var now = timeProvider.GetUtcNow();
                var removed = await db.DocumentDrafts.Where(draft => draft.ExpiresAtUtc <= now).ExecuteDeleteAsync(stoppingToken);
                if (removed > 0) logger.LogInformation("Removed {Count} expired document drafts.", removed);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Expired document drafts could not be removed.");
            }
        }
    }
}
