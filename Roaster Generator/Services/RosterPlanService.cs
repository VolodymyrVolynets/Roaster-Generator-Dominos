using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Roaster_Generator.Contracts.Roster;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;

namespace Roaster_Generator.Services;

public sealed class RosterPlanService(AppDbContext db, RosterInputService inputs)
{
    public async Task<RosterWeekSummaryResponse> GetSummaryAsync(int weekOffset, CancellationToken ct)
    {
        var weekStart = WeeklyScheduleService.GetWeekMonday(weekOffset);
        var ids = await db.Employees.AsNoTracking().Where(e => e.IsActive).Select(e => e.Id).ToListAsync(ct);
        var availability = await db.Shifts.AsNoTracking().Where(s => ids.Contains(s.EmployeeId) && s.Date >= weekStart && s.Date < weekStart.AddDays(7)).ToListAsync(ct);
        var demandExists = await db.DemandPlans.AnyAsync(ct);
        var required = 0;
        if (demandExists)
        {
            try { required = (await inputs.LoadAsync(weekStart, ct)).Input.Demand.Sum(d => d.RequiredDrivers); }
            catch (RosterInputException) { /* Generation supplies the detailed demand validation errors. */ }
        }
        return new RosterWeekSummaryResponse
        {
            WeekStart = weekStart, DemandPlanExists = demandExists, RequiredDriverHours = required,
            EnteredAvailabilityHours = availability.Sum(s => Duration(s.StartTime, s.FinishTime)),
            DriversWithoutAvailability = ids.Count - availability.Select(s => s.EmployeeId).Distinct().Count()
        };
    }

    public Task<RosterPlanResponse?> GetAsync(int weekOffset, CancellationToken ct) =>
        GetAsync(WeeklyScheduleService.GetWeekMonday(weekOffset), ct);

    public async Task<RosterPlanResponse?> GetAsync(DateOnly weekStart, CancellationToken ct)
    {
        var plan = await db.RosterPlans.AsNoTracking().Include(p => p.Shifts).ThenInclude(s => s.Employee)
            .SingleOrDefaultAsync(p => p.WeekStart == weekStart, ct);
        return plan is null ? null : ToResponse(plan);
    }

    public async Task<object> GetHistoryAsync(CancellationToken ct)
    {
        var plans = await db.RosterPlans.AsNoTracking().OrderByDescending(p => p.WeekStart)
            .Select(p => new { p.Id, p.WeekStart, p.UpdatedAtUtc, p.SnapshotJson }).Take(156).ToListAsync(ct);
        return plans.Select(p =>
        {
            var snapshot = p.SnapshotJson is null ? null : JsonSerializer.Deserialize<RosterPlanResponse>(p.SnapshotJson);
            return new { p.Id, p.WeekStart, p.UpdatedAtUtc, TotalDemandHours = snapshot?.TotalDemandHours,
                TotalScheduledHours = snapshot?.TotalScheduledHours, AverageHoursPerShift = snapshot?.AverageHoursPerShift,
                SolverStatus = snapshot?.SolverStatus ?? "legacy" };
        }).ToList();
    }

    // Caller owns the transaction; replacing shifts and the audit snapshot is one atomic save.
    public async Task<RosterPlanResponse> SaveAsync(LoadedRosterInput loaded, RosterSolverResult result, CancellationToken ct)
    {
        var validation = RosterSolver.Validate(loaded.Input, result.Shifts);
        if (!result.Success || validation.Count > 0)
            throw new RosterInputException("The generated roster failed final validation and was not saved.", validation);
        return await PersistValidatedAsync(loaded, result, ct);
    }

    public async Task<RosterPlanResponse> UpdateAsync(
        RosterPlanUpdateRequest request,
        CancellationToken ct)
    {
        if (request.WeekStart == default || request.WeekStart.DayOfWeek != DayOfWeek.Monday)
            throw new RosterInputException("Select the Monday of a saved roster week.");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var ownsLock = await db.Database
            .SqlQueryRaw<bool>("SELECT pg_try_advisory_xact_lock(724863910) AS \"Value\"")
            .SingleAsync(ct);
        if (!ownsLock)
            throw new RosterInputException("A roster is being generated or edited on this server. Retry after it finishes.");

        var exists = await db.RosterPlans
            .AsNoTracking()
            .AnyAsync(plan => plan.WeekStart == request.WeekStart, ct);
        if (!exists)
            throw new RosterInputException("A roster has not been generated for this week.");

        var loaded = await inputs.LoadAsync(request.WeekStart, ct);
        var shifts = (request.Shifts ?? [])
            .Select(shift => new RosterSolverShift(
                shift.EmployeeId,
                shift.Date,
                shift.StartHour,
                shift.FinishHour))
            .ToArray();
        var validation = RosterSolver.Validate(loaded.Input, shifts);
        if (validation.Count > 0)
            throw new RosterInputException("The edited roster is invalid and was not saved.", validation);

        var result = new RosterSolverResult
        {
            Status = "manual",
            Message = "Roster manually updated by an administrator.",
            Shifts = shifts,
            Diagnostics = ["Roster manually updated by an administrator."],
            TotalDemandHours = loaded.Input.Demand.Sum(demand => demand.RequiredDrivers),
            TotalScheduledHours = shifts.Sum(shift => shift.DurationHours)
        };
        var response = await PersistValidatedAsync(loaded, result, ct);
        await transaction.CommitAsync(ct);
        return response;
    }

    private async Task<RosterPlanResponse> PersistValidatedAsync(
        LoadedRosterInput loaded,
        RosterSolverResult result,
        CancellationToken ct)
    {
        var plan = await db.RosterPlans.Include(p => p.Shifts).SingleOrDefaultAsync(p => p.WeekStart == loaded.Input.WeekStart, ct);
        var now = DateTimeOffset.UtcNow;
        if (plan is null)
        {
            plan = new RosterPlan { Id = Guid.NewGuid(), WeekStart = loaded.Input.WeekStart, CreatedAtUtc = now };
            db.RosterPlans.Add(plan);
        }
        else
        {
            db.RosterShifts.RemoveRange(plan.Shifts);
            plan.Shifts.Clear();
            // Flush deletions before inserts to avoid colliding with existing unique shift keys.
            await db.SaveChangesAsync(ct);
        }
        plan.UpdatedAtUtc = now;
        foreach (var shift in result.Shifts)
        {
            var entity = new RosterShift
            {
                Id = Guid.NewGuid(), EmployeeId = shift.EmployeeId, RosterPlanId = plan.Id, Date = shift.Date,
                StartTime = new TimeOnly(shift.StartHour % 24, 0), FinishTime = new TimeOnly(shift.FinishHour % 24, 0)
            };
            // Explicit Added state is essential when replacing a tracked existing plan:
            // generated, non-empty GUIDs would otherwise be discovered as existing rows.
            db.RosterShifts.Add(entity);
            plan.Shifts.Add(entity);
        }
        var response = BuildResponse(plan, loaded, result);
        plan.SnapshotJson = JsonSerializer.Serialize(response);
        await db.SaveChangesAsync(ct);
        return response;
    }

    private static RosterPlanResponse BuildResponse(RosterPlan plan, LoadedRosterInput loaded, RosterSolverResult result)
    {
        var history = (loaded.Input.History ?? []).Where(h => h.TargetHours > 0).ToList();
        var eligible = loaded.Input.Employees.Where(e => e.TargetHours > 0).Select(e => e.Id).ToHashSet();
        var historyTargetTotal = history.Where(h => eligible.Contains(h.EmployeeId)).Sum(h => h.TargetHours);
        var historyHoursTotal = history.Where(h => eligible.Contains(h.EmployeeId)).Sum(h => h.ScheduledHours);
        var currentTargetTotal = loaded.Input.Employees.Where(e => e.TargetHours > 0).Sum(e => e.TargetHours);
        var currentRatio = currentTargetTotal > 0 ? loaded.Input.Demand.Sum(d => d.RequiredDrivers) / (double)currentTargetTotal : 0;
        var historicalRatio = historyTargetTotal > 0 ? historyHoursTotal / (double)historyTargetTotal : 0;
        var employees = loaded.Input.Employees.OrderBy(e => e.LastName).ThenBy(e => e.FirstName).Select(e =>
        {
            var shifts = result.Shifts.Where(s => s.EmployeeId == e.Id).OrderBy(s => s.Date).ThenBy(s => s.StartHour).ToList();
            var hours = shifts.Sum(s => s.DurationHours);
            var past = history.Where(h => h.EmployeeId == e.Id).ToList();
            var pastHours = past.Sum(h => h.ScheduledHours);
            var pastTargets = past.Sum(h => h.TargetHours);
            var correction = loaded.Settings.HistoryFairnessWeight > 0 && pastTargets > 0
                ? Math.Clamp((historicalRatio - pastHours / (double)pastTargets) / past.Count, -0.15, 0.15) : 0;
            return new RosterEmployeeResponse
            {
                EmployeeId = e.Id, EmployeeName = $"{e.FirstName} {e.LastName}".Trim(), TargetHours = e.TargetHours,
                ScheduledHours = hours, TargetPercentage = e.TargetHours > 0 ? Math.Round(100d * hours / e.TargetHours, 2) : null,
                PreviousScheduledHours = pastHours, PreviousTargetHours = pastTargets, HistoryWeeks = past.Count,
                PreviousTargetPercentage = pastTargets > 0 ? Math.Round(100d * pastHours / pastTargets, 2) : null,
                BalancedTargetHours = e.TargetHours > 0 ? Math.Round(Math.Max(0, e.TargetHours * (currentRatio + correction)), 2) : null,
                CumulativeTargetPercentage = e.TargetHours > 0 ? Math.Round(100d * (pastHours + hours) / (pastTargets + e.TargetHours), 2) : null,
                Shifts = shifts.Select(s => new RosterShiftResponse
                {
                    Date = s.Date, StartTime = $"{s.StartHour % 24:00}:00", FinishTime = $"{s.FinishHour % 24:00}:00",
                    StartDayOffset = s.StartHour / 24, FinishDayOffset = s.FinishHour / 24, DurationHours = s.DurationHours
                }).ToList()
            };
        }).ToList();
        var percentages = employees.Where(e => e.TargetPercentage.HasValue).Select(e => e.TargetPercentage!.Value).ToList();
        var cumulativePercentages = employees.Where(e => e.CumulativeTargetPercentage.HasValue).Select(e => e.CumulativeTargetPercentage!.Value).ToList();
        var spread = percentages.Count > 0 ? Math.Round(percentages.Max() - percentages.Min(), 2) : 0;
        var rests = new List<double>();
        foreach (var employee in loaded.Input.Employees)
        {
            var shifts = result.Shifts.Where(s => s.EmployeeId == employee.Id)
                .Select(s => new { Start = s.Date.ToDateTime(TimeOnly.MinValue).AddHours(s.StartHour), Finish = s.Date.ToDateTime(TimeOnly.MinValue).AddHours(s.FinishHour), Generated = true })
                .Concat(loaded.Input.BoundaryShifts.Where(s => s.EmployeeId == employee.Id).Select(s => new { s.Start, s.Finish, Generated = false }))
                .OrderBy(s => s.Start).ToList();
            for (var i = 1; i < shifts.Count; i++)
                if (shifts[i].Generated || shifts[i - 1].Generated)
                    rests.Add((shifts[i].Start - shifts[i - 1].Finish).TotalHours);
        }
        var warnings = loaded.Warnings.Concat(result.Diagnostics).ToList();
        if (result.Status == "feasible") warnings.Add(result.Message);
        foreach (var employee in employees.Where(e => e.TargetHours > 0))
        {
            var availabilityLimit = loaded.Input.Availability.Where(a => a.EmployeeId == employee.EmployeeId)
                .Sum(a => Duration(a.StartTime, a.FinishTime) is var duration && duration >= 3 ? Math.Min(10, duration) : 0);
            var equalShare = currentTargetTotal > 0 ? employee.TargetHours * loaded.Input.Demand.Sum(d => d.RequiredDrivers) / (double)currentTargetTotal : 0;
            if (availabilityLimit < equalShare)
                warnings.Add($"{employee.EmployeeName}: availability allows at most {availabilityLimit} hours before rest and demand checks, below the equal-percentage share of {equalShare:F1} hours. More availability is needed to close this fairness gap.");
        }
        if (spread > 30 && !warnings.Any(warning => warning.StartsWith("Fairness warning:", StringComparison.Ordinal)))
        {
            var least = employees.Where(e => e.TargetPercentage.HasValue).MinBy(e => e.TargetPercentage)!;
            var most = employees.Where(e => e.TargetPercentage.HasValue).MaxBy(e => e.TargetPercentage)!;
            warnings.Add($"Fairness check: this week's target-percentage gap is {spread:F1} percentage points ({least.EmployeeName}: {least.TargetPercentage:F1}%; {most.EmployeeName}: {most.TargetPercentage:F1}%). Review unavailable drivers, prior-week compensation and shift/rest constraints. Increasing fairness weights or the solve budget may improve the gap; exact coverage remains mandatory.");
        }
        return new RosterPlanResponse
        {
            Id = plan.Id, WeekStart = plan.WeekStart, UpdatedAtUtc = plan.UpdatedAtUtc,
            TotalDemandHours = loaded.Input.Demand.Sum(d => d.RequiredDrivers), TotalScheduledHours = result.Shifts.Sum(s => s.DurationHours),
            CoveragePercent = 100, SolverStatus = result.Status, IsOptimal = result.Status == "optimal", SolveSeconds = result.WallTimeSeconds,
            Settings = loaded.Settings, Employees = employees, Warnings = warnings,
            MinimumRestHours = rests.Count > 0 ? rests.Min() : null,
            FairnessSpreadPercentagePoints = spread,
            HistoricalFairnessSpreadPercentagePoints = cumulativePercentages.Count > 0 ? Math.Round(cumulativePercentages.Max() - cumulativePercentages.Min(), 2) : 0,
            Coverage = loaded.Input.Demand.OrderBy(d => d.Date).ThenBy(d => d.Hour).Select(d => new RosterCoverageResponse
            {
                Date = d.Date, StartTime = $"{d.Hour % 24:00}:00", StartDayOffset = d.Hour / 24, Required = d.RequiredDrivers,
                Scheduled = result.Shifts.Count(s => s.Date == d.Date && s.StartHour <= d.Hour && s.FinishHour > d.Hour)
            }).ToList()
        };
    }

    private static RosterPlanResponse ToResponse(RosterPlan plan)
    {
        if (plan.SnapshotJson is not null)
            return JsonSerializer.Deserialize<RosterPlanResponse>(plan.SnapshotJson)
                ?? throw new InvalidOperationException("The saved roster snapshot cannot be read.");
        var employees = plan.Shifts.GroupBy(s => s.Employee).OrderBy(g => g.Key.LastName).Select(g => new RosterEmployeeResponse
        {
            EmployeeId = g.Key.Id, EmployeeName = $"{g.Key.FirstName} {g.Key.LastName}".Trim(), TargetHours = g.Key.TargetHours,
            ScheduledHours = g.Sum(s => Duration(s.StartTime, s.FinishTime)),
            TargetPercentage = g.Key.TargetHours > 0 ? 100d * g.Sum(s => Duration(s.StartTime, s.FinishTime)) / g.Key.TargetHours : null,
            Shifts = g.OrderBy(s => s.Date).Select(s => new RosterShiftResponse
            {
                Date = s.Date, StartTime = s.StartTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                FinishTime = s.FinishTime.ToString("HH:mm", CultureInfo.InvariantCulture), DurationHours = Duration(s.StartTime, s.FinishTime),
                StartDayOffset = s.StartTime.Hour < 6 ? 1 : 0,
                FinishDayOffset = s.StartTime.Hour < 6 || s.FinishTime <= s.StartTime ? 1 : 0
            }).ToList()
        }).ToList();
        return new RosterPlanResponse
        {
            Id = plan.Id, WeekStart = plan.WeekStart, UpdatedAtUtc = plan.UpdatedAtUtc,
            TotalScheduledHours = employees.Sum(e => e.ScheduledHours), Employees = employees,
            Warnings = ["Demand snapshot unavailable for this older roster; coverage cannot be verified."]
        };
    }

    private static int Duration(TimeOnly start, TimeOnly finish) => (finish.Hour - start.Hour + 24) % 24;
}
