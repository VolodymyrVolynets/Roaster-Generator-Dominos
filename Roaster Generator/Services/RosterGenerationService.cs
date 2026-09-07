using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Roaster_Generator.Contracts.Roster;
using Roaster_Generator.Hubs;

namespace Roaster_Generator.Services;

public sealed class RosterTimerAlreadyRunningException : Exception
{
    public RosterTimerAlreadyRunningException()
        : base("A WebSocket timer is already running for the selected week.")
    {
    }
}

public sealed class RosterTimerService(
    IHubContext<RosterTimerHub> hub,
    ILogger<RosterTimerService> logger)
{
    private const int TimerSeconds = 10;
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
                "rosterTimerProgress",
                log);
        }
    }

    public RosterTimerStartResponse Start(int weekOffset)
    {
        var weekStart = WeeklyScheduleService.GetWeekMonday(weekOffset);
        var jobId = Guid.NewGuid();

        var activeJob = new ActiveJob(jobId, weekOffset, weekStart);

        if (!activeJobs.TryAdd(weekStart, activeJob))
        {
            throw new RosterTimerAlreadyRunningException();
        }

        _ = RunAsync(activeJob);

        return new RosterTimerStartResponse
        {
            JobId = jobId,
            WeekOffset = weekOffset,
            WeekStart = weekStart,
            Status = "started",
            Stage = "queued",
            Progress = 0,
            Message = "WebSocket timer started."
        };
    }

    public bool Cancel(int weekOffset, Guid? jobId = null)
    {
        var weekStart = WeeklyScheduleService.GetWeekMonday(weekOffset);

        if (!activeJobs.TryGetValue(weekStart, out var activeJob) ||
            (jobId.HasValue && activeJob.JobId != jobId.Value))
        {
            return false;
        }

        try
        {
            activeJob.CancellationTokenSource.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private async Task RunAsync(ActiveJob activeJob)
    {
        var jobId = activeJob.JobId;
        var weekOffset = activeJob.WeekOffset;
        var weekStart = activeJob.WeekStart;
        var cancellationToken = activeJob.CancellationTokenSource.Token;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            await PublishAsync(
                jobId,
                weekOffset,
                weekStart,
                "started",
                "initializing",
                0,
                "WebSocket timer started.");

            await PublishAsync(
                jobId,
                weekOffset,
                weekStart,
                "running",
                "timer",
                0,
                $"Waiting {TimerSeconds} seconds; no roster will be generated.");

            for (var second = 1; second <= TimerSeconds; second++)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

                await PublishAsync(
                    jobId,
                    weekOffset,
                    weekStart,
                    "running",
                    "timer",
                    second * 100 / TimerSeconds,
                    $"WebSocket timer: {second}/{TimerSeconds} second(s) elapsed.");
            }

            await PublishAsync(
                jobId,
                weekOffset,
                weekStart,
                "completed",
                "timer-completed",
                100,
                $"The {TimerSeconds}-second WebSocket timer completed. No roster was generated or saved.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("WebSocket timer job {JobId} was cancelled.", jobId);

            await PublishAsync(
                jobId,
                weekOffset,
                weekStart,
                "cancelled",
                "cancelled",
                0,
                "The WebSocket timer was cancelled because the timer page was closed.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "WebSocket timer job {JobId} failed.", jobId);

            await PublishAsync(
                jobId,
                weekOffset,
                weekStart,
                "failed",
                "failed",
                0,
                "WebSocket timer failed.");
        }
        finally
        {
            activeJobs.TryRemove(weekStart, out _);
            activeJob.CancellationTokenSource.Dispose();
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
        var payload = new RosterTimerProgressResponse
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
            "rosterTimerProgress",
            payload);
    }

    private sealed class ActiveJob(Guid jobId, int weekOffset, DateOnly weekStart)
    {
        public Guid JobId { get; } = jobId;

        public int WeekOffset { get; } = weekOffset;

        public DateOnly WeekStart { get; } = weekStart;

        public ConcurrentQueue<RosterTimerProgressResponse> Logs { get; } = new();

        public CancellationTokenSource CancellationTokenSource { get; } = new();
    }
}
