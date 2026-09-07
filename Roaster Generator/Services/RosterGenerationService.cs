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

                if (second < DurationInSeconds)
                {
                    await PublishAsync(
                        jobId,
                        weekOffset,
                        weekStart,
                        "running",
                        second * 10,
                        $"Preparing roster generation… {second} of {DurationInSeconds} seconds.");
                    continue;
                }

                await PublishAsync(
                    jobId,
                    weekOffset,
                    weekStart,
                    "running",
                    90,
                    "Selecting exact shifts from demand and availability.");

                await using var scope = scopeFactory.CreateAsyncScope();
                var algorithm = scope.ServiceProvider.GetRequiredService<RosterGenerationAlgorithm>();
                var plan = await algorithm.GenerateAsync(weekStart, CancellationToken.None);
                var totalScheduledHours = plan.Shifts.Sum(GetDurationHours);

                await PublishAsync(
                    jobId,
                    weekOffset,
                    weekStart,
                    "completed",
                    100,
                    $"Roster generated with {totalScheduledHours} driver-hours.",
                    plan.Id,
                    totalScheduledHours);
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
        int progress,
        string message,
        Guid? rosterPlanId = null,
        int? totalScheduledHours = null) =>
        hub.Clients.All.SendAsync(
            "rosterGenerationProgress",
            new RosterGenerationProgressResponse
            {
                JobId = jobId,
                WeekOffset = weekOffset,
                WeekStart = weekStart,
                Status = status,
                Progress = progress,
                Message = message,
                RosterPlanId = rosterPlanId,
                TotalScheduledHours = totalScheduledHours
            });

    private static int GetDurationHours(RosterShift shift)
    {
        var start = shift.StartTime.Hour;
        var finish = shift.FinishTime.Hour;
        return finish > start ? finish - start : 24 - start + finish;
    }
}
