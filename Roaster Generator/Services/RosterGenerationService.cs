using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Roaster_Generator.Contracts.Roster;
using Roaster_Generator.Entities;
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
    ILogger<RosterGenerationService> logger,
    IServiceScopeFactory scopeFactory)
{
    private readonly ConcurrentDictionary<DateOnly, ActiveJob> activeJobs = new();

    public async Task SendActiveLogsAsync(string connectionId)
    {
        var logs = activeJobs.Values
            .SelectMany(job => job.Logs.ToArray())
            .OrderBy(log => log.TimestampUtc)
            .ToList();

        foreach (var log in logs)
        {
            await hub.Clients.Client(connectionId).SendAsync(
                "rosterGenerationProgress",
                log);
        }
    }

    public RosterGenerationStartResponse Start(int weekOffset)
    {
        var weekStart = WeeklyScheduleService.GetWeekMonday(weekOffset);
        var jobId = Guid.NewGuid();

        var activeJob = new ActiveJob(jobId, weekOffset, weekStart);

        if (!activeJobs.TryAdd(weekStart, activeJob))
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
            Stage = "queued",
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
                "initializing",
                0,
                "Roster generation started.");

            await PublishAsync(
                jobId,
                weekOffset,
                weekStart,
                "running",
                "initializing",
                2,
                "Initializing roster generation.");

            await using var scope = scopeFactory.CreateAsyncScope();
            var algorithm = scope.ServiceProvider.GetRequiredService<RosterGenerationAlgorithm>();

            var plan = await algorithm.GenerateAsync(
                weekStart,
                CancellationToken.None,
                (stage, progress, message) => PublishAsync(
                    jobId,
                    weekOffset,
                    weekStart,
                    "running",
                    stage,
                    progress,
                    message));
            var totalScheduledHours = plan.Shifts.Sum(GetDurationHours);

            await PublishAsync(
                jobId,
                weekOffset,
                weekStart,
                "completed",
                "completed",
                100,
                $"Roster generated with {totalScheduledHours} driver-hours.",
                plan.Id,
                totalScheduledHours);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Roster generation job {JobId} failed.", jobId);

            await PublishAsync(
                jobId,
                weekOffset,
                weekStart,
                "failed",
                "failed",
                0,
                exception is RosterGenerationException
                    ? exception.Message
                    : "Roster generation failed.");
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
        string stage,
        int progress,
        string message,
        Guid? rosterPlanId = null,
        int? totalScheduledHours = null)
    {
        var payload = new RosterGenerationProgressResponse
        {
            JobId = jobId,
            WeekOffset = weekOffset,
            WeekStart = weekStart,
            Status = status,
            Stage = stage,
            Progress = progress,
            Message = message,
            TimestampUtc = DateTimeOffset.UtcNow,
            RosterPlanId = rosterPlanId,
            TotalScheduledHours = totalScheduledHours
        };

        if (activeJobs.TryGetValue(weekStart, out var activeJob) && activeJob.JobId == jobId)
        {
            activeJob.Logs.Enqueue(payload);
        }

        return hub.Clients.All.SendAsync(
            "rosterGenerationProgress",
            payload);
    }

    private static int GetDurationHours(RosterShift shift)
    {
        var start = shift.StartTime.Hour;
        var finish = shift.FinishTime.Hour;
        return finish > start ? finish - start : 24 - start + finish;
    }

    private sealed class ActiveJob(Guid jobId, int weekOffset, DateOnly weekStart)
    {
        public Guid JobId { get; } = jobId;

        public int WeekOffset { get; } = weekOffset;

        public DateOnly WeekStart { get; } = weekStart;

        public ConcurrentQueue<RosterGenerationProgressResponse> Logs { get; } = new();
    }
}
