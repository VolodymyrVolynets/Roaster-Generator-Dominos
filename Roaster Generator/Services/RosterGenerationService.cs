using System.Collections.Concurrent;
using System.Data;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Roaster_Generator.Contracts.Roster;
using Roaster_Generator.Data;
using Roaster_Generator.Hubs;

namespace Roaster_Generator.Services;

public sealed class RosterTimerAlreadyRunningException : Exception
{
    public RosterTimerAlreadyRunningException() : base("A roster is already being generated. Wait for it to finish or cancel it before starting another week.") { }
}

// One worker and one solver thread protect small servers. Logs survive tab changes and reconnects.
public sealed class RosterTimerService(IServiceScopeFactory scopes, IHubContext<RosterTimerHub> hub,
    ILogger<RosterTimerService> logger) : BackgroundService
{
    private readonly object gate = new();
    private readonly Dictionary<DateOnly, ActiveJob> latestJobs = new();
    private readonly Channel<ActiveJob> jobs = Channel.CreateBounded<ActiveJob>(1);
    private readonly Channel<RosterTimerProgressResponse> broadcasts = Channel.CreateBounded<RosterTimerProgressResponse>(
        new BoundedChannelOptions(1024) { FullMode = BoundedChannelFullMode.DropOldest });
    private ActiveJob? active;

    public IReadOnlyList<RosterTimerProgressResponse> GetLogs(DateOnly weekStart)
    {
        lock (gate) return latestJobs.TryGetValue(weekStart, out var job) ? job.Logs.ToArray() : [];
    }

    public async Task SendActiveLogsAsync(string connectionId)
    {
        RosterTimerProgressResponse[] logs;
        lock (gate) logs = latestJobs.Values.SelectMany(j => j.Logs).OrderBy(l => l.TimestampUtc).ToArray();
        foreach (var log in logs)
            await hub.Clients.Client(connectionId).SendAsync("rosterGenerationProgress", log);
    }

    public RosterTimerStartResponse Start(int weekOffset)
    {
        ActiveJob job;
        lock (gate)
        {
            if (active is not null) throw new RosterTimerAlreadyRunningException();
            job = new ActiveJob(Guid.NewGuid(), weekOffset, WeeklyScheduleService.GetWeekMonday(weekOffset));
            active = job;
            latestJobs[job.WeekStart] = job;
            if (latestJobs.Count > 32)
                latestJobs.Remove(latestJobs.Where(p => p.Value != job).MinBy(p => p.Value.StartedAt).Key);
            Publish(job, "started", "queued", 0, "Roster generation queued. Only a roster with exact hourly coverage will be saved.");
            jobs.Writer.TryWrite(job);
        }
        return new RosterTimerStartResponse { JobId = job.JobId, WeekOffset = weekOffset, WeekStart = job.WeekStart,
            Status = "started", Stage = "queued", Progress = 0, Message = "Roster generation queued.", TimestampUtc = job.StartedAt, Sequence = 1 };
    }

    public bool Cancel(int weekOffset, Guid? jobId = null)
    {
        lock (gate)
        {
            if (active is null || active.WeekStart != WeeklyScheduleService.GetWeekMonday(weekOffset)
                || (jobId.HasValue && active.JobId != jobId)) return false;
            active.Cancellation.Cancel();
            return true;
        }
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(WorkAsync(stoppingToken), BroadcastAsync(stoppingToken));

    private async Task WorkAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var job in jobs.Reader.ReadAllAsync(stoppingToken))
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, job.Cancellation.Token);
                try { await RunAsync(job, linked.Token); }
                finally
                {
                    lock (gate) { active = null; job.Cancellation.Dispose(); }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private async Task BroadcastAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var log in broadcasts.Reader.ReadAllAsync(ct))
            {
                try { await hub.Clients.All.SendAsync("rosterGenerationProgress", log, ct); }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                { logger.LogWarning(ex, "WebSocket delivery failed for roster job {JobId}; log remains available for replay.", log.JobId); }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private async Task RunAsync(ActiveJob job, CancellationToken ct)
    {
        var stage = "loading";
        try
        {
            Publish(job, "running", stage, 2, "Loading the weekly demand template, employee targets, availability and adjacent saved shifts.");
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var inputs = scope.ServiceProvider.GetRequiredService<RosterInputService>();
            var plans = scope.ServiceProvider.GetRequiredService<RosterPlanService>();
            // Transaction advisory lock also prevents concurrent roster writes across API replicas.
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            var ownsLock = await db.Database.SqlQueryRaw<bool>("SELECT pg_try_advisory_xact_lock(724863910) AS \"Value\"").SingleAsync(ct);
            if (!ownsLock) throw new RosterInputException("Another server is generating a roster. Retry after that job finishes.");
            var loaded = await inputs.LoadAsync(job.WeekStart, ct);
            foreach (var warning in loaded.Warnings) Publish(job, "running", "input-check", 5, warning, "warning");
            Publish(job, "running", "input-check", 8,
                $"Loaded {loaded.Input.Employees.Count} active employees and {loaded.Input.Demand.Sum(d => d.RequiredDrivers)} required driver-hours. Latest shift start 22:00 (overnight finishes allowed). Minimum rest {loaded.Settings.MinimumRestHours}h; preferred rest {loaded.Settings.PreferredRestHours}h; solver budget {loaded.Settings.MaxSolveSeconds}s, one CPU worker.");
            var history = loaded.Input.History ?? [];
            Publish(job, "running", "fairness-history", 8,
                $"Fairness uses {history.Select(h => h.WeekStart).Distinct().Count()} saved week(s) from {job.WeekStart.AddDays(-28):yyyy-MM-dd} through {job.WeekStart.AddDays(-1):yyyy-MM-dd}. Missing weeks are not counted as zero-hour work. Current allocation weight {loaded.Settings.TargetHoursWeight}, history weight {loaded.Settings.HistoryFairnessWeight}, percentage-gap weight {loaded.Settings.FairnessSpreadWeight}.");
            foreach (var employee in loaded.Input.Employees.Where(e => e.TargetHours > 0))
            {
                var previous = history.Where(h => h.EmployeeId == employee.Id && h.TargetHours > 0).ToList();
                if (previous.Count > 0)
                    Publish(job, "running", "fairness-history", 8,
                        $"{employee.FirstName} {employee.LastName}: previous {previous.Count} saved week(s), {previous.Sum(h => h.ScheduledHours)}/{previous.Sum(h => h.TargetHours)} target hours ({100d * previous.Sum(h => h.ScheduledHours) / previous.Sum(h => h.TargetHours):F1}%). This history is balanced against this week's allocation.");
            }
            stage = "solving";
            var result = await Task.Run(() => new RosterSolver().Solve(loaded.Input,
                p => Publish(job, "running", p.Stage, Math.Clamp(p.Progress, 9, 89), p.Message), ct), ct);
            ct.ThrowIfCancellationRequested();
            if (!result.Success)
            {
                Publish(job, "failed", result.Status, 100, result.Message, "error", result.Diagnostics);
                return;
            }
            stage = "validating";
            Publish(job, "running", stage, 91, "Rechecking every demand hour, availability, shift duration, supervision and rest before saving.");
            var validation = RosterSolver.Validate(loaded.Input, result.Shifts);
            if (validation.Count > 0) throw new RosterInputException("Final roster validation failed; no roster was saved.", validation);
            var current = await inputs.LoadAsync(job.WeekStart, ct);
            if (current.Fingerprint != loaded.Fingerprint)
                throw new RosterInputException("Demand, availability, employee targets, settings or an adjacent roster changed during generation. Run generation again using the updated inputs. The previous saved roster was kept.");
            stage = "saving";
            Publish(job, "running", stage, 96, "Exact coverage verified. Saving the roster and its settings, demand and employee snapshot to the database.");
            var saved = await plans.SaveAsync(loaded, result, ct);
            Publish(job, "running", "fairness-check", 98,
                $"Fairness checked: current target-percentage gap {saved.FairnessSpreadPercentagePoints:F1} points; gap including the previous four weeks {saved.HistoricalFairnessSpreadPercentagePoints:F1} points. Every employee's allocation and history will be shown with the saved roster.",
                saved.FairnessSpreadPercentagePoints > 30 ? "warning" : "info");
            ct.ThrowIfCancellationRequested();
            // Once committing starts, complete it and report the actual durable outcome, even if cancel arrives.
            await transaction.CommitAsync(CancellationToken.None);
            Publish(job, "completed", "saved", 100,
                $"Roster saved: {saved.TotalScheduledHours}/{saved.TotalDemandHours} driver-hours, 100% exact coverage. Average {saved.AverageHoursPerShift:F2} hours per shift. {(saved.IsOptimal ? "Best weighted preference score proven." : "Valid roster found; preference optimization stopped at the time limit.")} Open Saved rosters to review.",
                "info", saved.Warnings, saved.Id, saved.TotalScheduledHours);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        { Publish(job, "cancelled", "cancelled", 100, "Generation cancelled. No new roster was saved; the previous saved roster was kept.", "warning"); }
        catch (RosterInputException ex)
        { Publish(job, "failed", stage, 100, ex.Message, "error", ex.Diagnostics); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Roster job {JobId} failed during {Stage}.", job.JobId, stage);
            var reason = ex switch
            {
                Npgsql.PostgresException pg => $"Database operation failed (PostgreSQL {pg.SqlState}).",
                Npgsql.NpgsqlException => "The database connection failed. Check that PostgreSQL is available.",
                DbUpdateConcurrencyException => "A roster row changed during saving. Retry generation; the replacement transaction was rolled back.",
                DbUpdateException => "The database rejected the roster save. Check database constraints and the server log.",
                DllNotFoundException => "The native scheduling library could not load. Install the matching OR-Tools runtime for this server.",
                _ => $"An unexpected {ex.GetType().Name} occurred. The server log contains the technical details."
            };
            Publish(job, "failed", stage, 100, $"Generation failed during {stage}. {reason} Reference: {job.JobId}. No success was reported; reload Saved rosters to check the stored state.", "error");
        }
    }

    private void Publish(ActiveJob job, string status, string stage, int progress, string message,
        string severity = "info", IReadOnlyList<string>? diagnostics = null, Guid? rosterPlanId = null, int? totalScheduledHours = null)
    {
        lock (gate)
        {
            var payload = new RosterTimerProgressResponse
            {
                JobId = job.JobId, WeekOffset = job.WeekOffset, WeekStart = job.WeekStart, Status = status, Stage = stage,
                Progress = progress, Message = message, TimestampUtc = DateTimeOffset.UtcNow, Sequence = ++job.Sequence,
                Severity = severity, Diagnostics = diagnostics ?? [], RosterPlanId = rosterPlanId, TotalScheduledHours = totalScheduledHours
            };
            job.Logs.Enqueue(payload);
            while (job.Logs.Count > 1024) job.Logs.TryDequeue(out _);
            broadcasts.Writer.TryWrite(payload);
        }
    }

    private sealed class ActiveJob(Guid jobId, int weekOffset, DateOnly weekStart)
    {
        public Guid JobId { get; } = jobId;
        public int WeekOffset { get; } = weekOffset;
        public DateOnly WeekStart { get; } = weekStart;
        public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
        public ConcurrentQueue<RosterTimerProgressResponse> Logs { get; } = new();
        public CancellationTokenSource Cancellation { get; } = new();
        public long Sequence { get; set; }
    }
}
