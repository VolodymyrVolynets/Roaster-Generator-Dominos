using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Roaster_Generator.Contracts.Roster;
using Roaster_Generator.Hubs;

namespace Roaster_Generator.Services;

public sealed class RosterGenerationAlreadyRunningException : Exception
{
    public RosterGenerationAlreadyRunningException()
        : base("Roster generation is already running for the selected week.")
    {
    }
}

public sealed class RosterGenerationService(
    IHubContext<RosterGenerationHub> hub,
    ILogger<RosterGenerationService> logger)
{
    private const int DurationInSeconds = 10;
    private readonly ConcurrentDictionary<DateOnly, Guid> activeJobs = new();

    public RosterGenerationStartResponse Start(int weekOffset)
    {
        var weekStart = WeeklyScheduleService.GetWeekMonday(weekOffset);
        var jobId = Guid.NewGuid();

        if (!activeJobs.TryAdd(weekStart, jobId))
        {
            throw new RosterGenerationAlreadyRunningException();
        }

        _ = RunAsync(jobId, weekOffset, weekStart);

        return new RosterGenerationStartResponse
        {
            JobId = jobId,
            WeekOffset = weekOffset,
            WeekStart = weekStart,
            Status = "started",
            Progress = 0,
            Message = "Roster generation started."
        };
    }

    private async Task RunAsync(Guid jobId, int weekOffset, DateOnly weekStart)
    {
        try
        {
            await PublishAsync(
                jobId,
                weekOffset,
                weekStart,
                "started",
                0,
                "Roster generation started.");

            for (var second = 1; second <= DurationInSeconds; second++)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));

                var progress = second * 100 / DurationInSeconds;
                var status = second == DurationInSeconds ? "completed" : "running";
                var message = second == DurationInSeconds
                    ? "Roster generation completed."
                    : $"Generating roster… {second} of {DurationInSeconds} seconds.";

                await PublishAsync(jobId, weekOffset, weekStart, status, progress, message);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Roster generation job {JobId} failed.", jobId);

            await PublishAsync(
                jobId,
                weekOffset,
                weekStart,
                "failed",
                0,
                "Roster generation failed.");
        }
        finally
        {
            activeJobs.TryRemove(weekStart, out _);
        }
    }

    private Task PublishAsync(
        Guid jobId,
        int weekOffset,
        DateOnly weekStart,
        string status,
        int progress,
        string message) =>
        hub.Clients.All.SendAsync(
            "rosterGenerationProgress",
            new RosterGenerationProgressResponse
            {
                JobId = jobId,
                WeekOffset = weekOffset,
                WeekStart = weekStart,
                Status = status,
                Progress = progress,
                Message = message
            });
}
