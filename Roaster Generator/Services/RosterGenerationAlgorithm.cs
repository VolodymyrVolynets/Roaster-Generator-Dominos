using Google.OrTools.Sat;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Roaster_Generator.Configuration;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;

namespace Roaster_Generator.Services;

public sealed class RosterGenerationException(string message) : Exception(message);

public sealed class RosterGenerationAlgorithm(
    AppDbContext db,
    IOptions<ShopHoursOptions> shopHoursOptions,
    RosterGenerationSettingsService rosterSettings)
{
    private const int MinimumShiftHours = 3;
    private const int PreferredMinimumShiftHours = 6;
    private const int MaximumShiftHours = 10;
    private const int MinimumBreakMinutes = 7 * 60;
    private const int HoursInWeek = 7 * 24;
    private readonly ShopHoursOptions shopHours = shopHoursOptions.Value;

    public async Task<RosterPlan> GenerateAsync(
        DateOnly weekStart,
        CancellationToken cancellationToken,
        Func<string, int, string, Task>? reportProgress = null)
    {
        async Task ReportAsync(string stage, int progress, string message)
        {
            if (reportProgress is not null)
            {
                await reportProgress(stage, progress, message);
            }
        }

        await ReportAsync("loading", 5, "Loading the demand plan.");
        var demandPlan = await db.DemandPlans
            .Include(plan => plan.Columns)
            .Include(plan => plan.Rows)
            .ThenInclude(row => row.Values)
            .OrderByDescending(plan => plan.UpdatedAtUtc)
            .ThenByDescending(plan => plan.WeekStart)
            .FirstOrDefaultAsync(cancellationToken);

        if (demandPlan is null)
        {
            throw new RosterGenerationException(
                "Import the weekly demand before generating a roster.");
        }

        await ReportAsync("loading", 12, "Loading active employees and availability.");
        var employees = await db.Employees
            .AsNoTracking()
            .Where(employee => employee.IsActive)
            .OrderBy(employee => employee.LastName)
            .ThenBy(employee => employee.FirstName)
            .ToListAsync(cancellationToken);

        if (employees.Count == 0)
        {
            throw new RosterGenerationException("At least one active employee is required.");
        }

        var employeeIds = employees.Select(employee => employee.Id).ToArray();
        var availability = await db.Shifts
            .AsNoTracking()
            .Where(shift => employeeIds.Contains(shift.EmployeeId))
            .Where(shift => shift.Date >= weekStart && shift.Date < weekStart.AddDays(7))
            .ToListAsync(cancellationToken);

        await ReportAsync("preparing", 20, "Converting demand into hourly driver requirements.");
        cancellationToken.ThrowIfCancellationRequested();
        var demand = BuildDemand(demandPlan);
        var totalDemandHours = demand.Sum();

        if (totalDemandHours < employees.Count * MinimumShiftHours)
        {
            throw new RosterGenerationException(
                $"The selected demand contains {totalDemandHours} driver-hours, but {employees.Count} active employees require at least {employees.Count * MinimumShiftHours} hours in total.");
        }

        var candidates = GenerateCandidates(
            weekStart,
            employees,
            availability,
            demand,
            cancellationToken);
        var unrestrictedCandidates = GenerateCandidates(
            weekStart,
            employees,
            availability,
            demand,
            cancellationToken,
            enforceCanWorkAlone: false);

        await ReportAsync(
            "candidate-shifts",
            32,
            $"Generated {candidates.Count} valid candidate shifts from {availability.Count} availability entries.");

        if (candidates.Count == 0)
        {
            throw new RosterGenerationException(
                BuildGenerationFailureMessage(
                    weekStart,
                    demand,
                    candidates,
                    unrestrictedCandidates,
                    employees,
                    availability,
                    []));
        }

        var parameters = await rosterSettings.GetParametersAsync(cancellationToken);
        await ReportAsync(
            "constraint-optimization",
            38,
            "Starting CP-SAT constraint optimization using the configured roster weights.");
        var solver = new CpSatRosterSolver(candidates, demand, employees, parameters);
        var selectedCandidates = await solver.SolveAsync(cancellationToken, async (stage, progress, message) =>
            await ReportAsync(stage, progress, message));

        if (selectedCandidates is null)
        {
            throw new RosterGenerationException(
                BuildGenerationFailureMessage(
                    weekStart,
                    demand,
                    candidates,
                    unrestrictedCandidates,
                    employees,
                    availability,
                    solver.FailureSlots));
        }

        await ReportAsync("validation", 90, "Validating demand coverage and employee constraints.");
        var rosterPlan = await db.RosterPlans
            .Include(plan => plan.Shifts)
            .SingleOrDefaultAsync(plan => plan.WeekStart == weekStart, cancellationToken);

        if (rosterPlan is null)
        {
            rosterPlan = new RosterPlan
            {
                Id = Guid.NewGuid(),
                WeekStart = weekStart,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            db.RosterPlans.Add(rosterPlan);
        }
        else
        {
            db.RosterShifts.RemoveRange(rosterPlan.Shifts);
            rosterPlan.Shifts.Clear();
            rosterPlan.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        foreach (var candidate in selectedCandidates)
        {
            rosterPlan.Shifts.Add(new RosterShift
            {
                Id = Guid.NewGuid(),
                RosterPlanId = rosterPlan.Id,
                EmployeeId = candidate.EmployeeId,
                Date = candidate.Date,
                StartTime = candidate.StartTime,
                FinishTime = candidate.FinishTime
            });
        }

        await ReportAsync("saving", 96, "Saving the generated roster.");
        await db.SaveChangesAsync(cancellationToken);
        return rosterPlan;
    }

    private int[] BuildDemand(DemandPlan demandPlan)
    {
        var demand = new int[HoursInWeek];
        var columns = demandPlan.Columns.ToDictionary(column => column.Position);

        for (var position = 0; position < 7; position++)
        {
            if (!columns.ContainsKey(position))
            {
                throw new RosterGenerationException(
                    "The demand plan must contain Monday through Sunday columns.");
            }
        }

        foreach (var row in demandPlan.Rows)
        {
            if (row.Hour is < 0 or > 23)
            {
                throw new RosterGenerationException("Demand contains an invalid hour.");
            }

            foreach (var column in columns.Values.Where(column => column.Position is >= 0 and < 7))
            {
                var value = row.Values.FirstOrDefault(item => item.DemandColumnId == column.Id)?.Demand ?? 0;

                if (value < 0)
                {
                    throw new RosterGenerationException("Demand cannot be negative.");
                }

                demand[GetDemandSlot(column.Position, row.Hour)] = value;
            }
        }

        return demand;
    }

    private List<CandidateShift> GenerateCandidates(
        DateOnly weekStart,
        IReadOnlyList<Employee> employees,
        IReadOnlyList<Shift> availability,
        IReadOnlyList<int> demand,
        CancellationToken cancellationToken,
        bool enforceCanWorkAlone = true)
    {
        var employeeIndexes = employees
            .Select((employee, index) => (employee.Id, Index: index))
            .ToDictionary(item => item.Id, item => item.Index);
        var candidates = new List<CandidateShift>();

        foreach (var availabilityShift in availability)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!employeeIndexes.TryGetValue(availabilityShift.EmployeeId, out var employeeIndex) ||
                availabilityShift.StartTime.Minute != 0 ||
                availabilityShift.FinishTime.Minute != 0)
            {
                continue;
            }

            var dayIndex = availabilityShift.Date.DayNumber - weekStart.DayNumber;

            if (dayIndex is < 0 or >= 7)
            {
                continue;
            }

            var availabilityStart = dayIndex * 24 + availabilityShift.StartTime.Hour;
            var availabilityFinishHour = availabilityShift.FinishTime.Hour;

            if (availabilityFinishHour <= availabilityShift.StartTime.Hour)
            {
                availabilityFinishHour += 24;
            }

            var availabilityEnd = dayIndex * 24 + availabilityFinishHour;

            for (var start = availabilityStart;
                 start <= availabilityEnd - MinimumShiftHours;
                 start++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                for (var duration = MinimumShiftHours; duration <= MaximumShiftHours; duration++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var end = start + duration;

                    if (end > availabilityEnd)
                    {
                        continue;
                    }

                    var coveredSlots = new int[duration];
                    var isValid = true;

                    for (var offset = 0; offset < duration; offset++)
                    {
                        var slot = GetDemandSlotForActualHour(start + offset);

                        if (slot is null ||
                            demand[slot.Value] <= 0 ||
                            !IsShopOpen(weekStart, slot.Value))
                        {
                            isValid = false;
                            break;
                        }

                        coveredSlots[offset] = slot.Value;
                    }

                    if (!isValid)
                    {
                        continue;
                    }

                    if (enforceCanWorkAlone &&
                        !employees[employeeIndex].CanWorkAlone &&
                        coveredSlots.Any(slot => demand[slot] < 2))
                    {
                        continue;
                    }

                    candidates.Add(new CandidateShift(
                        employees[employeeIndex].Id,
                        employeeIndex,
                        weekStart.AddDays(start / 24),
                        TimeOnly.FromTimeSpan(TimeSpan.FromHours(start % 24)),
                        TimeOnly.FromTimeSpan(TimeSpan.FromHours(end % 24)),
                        duration,
                        start * 60,
                        end * 60,
                        coveredSlots));
                }
            }
        }

        return candidates
            .GroupBy(candidate =>
                (candidate.EmployeeId,
                 candidate.Date,
                 candidate.StartTime,
                 candidate.FinishTime))
            .Select(group => group.First())
            .ToList();
    }

    private string BuildGenerationFailureMessage(
        DateOnly weekStart,
        IReadOnlyList<int> demand,
        IReadOnlyList<CandidateShift> candidates,
        IReadOnlyList<CandidateShift> unrestrictedCandidates,
        IReadOnlyList<Employee> employees,
        IReadOnlyList<Shift> availability,
        IReadOnlyList<int> blockedSlots)
    {
        var diagnostics = new List<string>();
        var slotsToExplain = blockedSlots
            .Concat(Enumerable.Range(0, demand.Count).Where(slot => demand[slot] > 0))
            .Distinct()
            .Where(slot => demand[slot] > 0)
            .OrderBy(slot => slot)
            .ToList();

        foreach (var slot in slotsToExplain)
        {
            var requiredDrivers = demand[slot];
            var possibleCandidates = candidates
                .Where(candidate => candidate.CoveredSlots.Contains(slot))
                .ToList();
            var possibleUnrestrictedCandidates = unrestrictedCandidates
                .Where(candidate => candidate.CoveredSlots.Contains(slot))
                .ToList();
            var distinctEmployees = possibleCandidates
                .Select(candidate => candidate.EmployeeIndex)
                .Distinct()
                .Count();
            var isBlockedByCombination = blockedSlots.Contains(slot) &&
                possibleCandidates.Count > 0 &&
                distinctEmployees >= requiredDrivers;

            if (possibleCandidates.Count > 0 && !isBlockedByCombination && distinctEmployees >= requiredDrivers)
            {
                continue;
            }

            var (date, hour) = GetBusinessDateAndHour(weekStart, slot);
            var location = $"{date:yyyy-MM-dd} {hour:00}:00";
            var reason = possibleCandidates.Count == 0
                ? possibleUnrestrictedCandidates.Count > 0
                    ? "only non-solo or otherwise restricted driver shifts are available"
                    : "no valid continuous 3–10 hour shift is available from the submitted availability and shop hours"
                : isBlockedByCombination
                    ? "all candidate shifts conflict with another required shift or the 7-hour employee break"
                    : $"requires {requiredDrivers} drivers, but only {distinctEmployees} different employees can cover it";
            var examples = FormatCandidateExamples(
                possibleCandidates.Count > 0 ? possibleCandidates : possibleUnrestrictedCandidates,
                employees);
            var availabilityExamples = examples.Length == 0
                ? FormatAvailabilityExamples(date, availability, employees)
                : string.Empty;

            diagnostics.Add(
                $"{location}: demand {requiredDrivers}; {reason}." +
                (examples.Length == 0 ? string.Empty : $" Candidate shifts: {examples}.") +
                (availabilityExamples.Length == 0
                    ? string.Empty
                    : $" Availability entered: {availabilityExamples}."));

            if (diagnostics.Count >= 8)
            {
                break;
            }
        }

        if (diagnostics.Count == 0)
        {
            var employeesWithoutCandidates = employees
                .Where(employee => candidates.All(candidate => candidate.EmployeeId != employee.Id))
                .Select(GetEmployeeName)
                .ToList();

            if (employeesWithoutCandidates.Count > 0)
            {
                diagnostics.Add(
                    "Employees required to receive at least 3 hours but with no valid shift candidates: " +
                    string.Join(", ", employeesWithoutCandidates) + ".");
            }

            diagnostics.Add(
                "CP-SAT found enough individual candidates at some periods, but no complete combination satisfies all demand, shift-overlap, 7-hour-break, minimum-hours, and solo-driver constraints.");

            var detailedSlots = slotsToExplain
                .Select(slot =>
                {
                    var slotCandidates = candidates
                        .Where(candidate => candidate.CoveredSlots.Contains(slot))
                        .ToList();
                    var employeeCount = slotCandidates
                        .Select(candidate => candidate.EmployeeIndex)
                        .Distinct()
                        .Count();

                    return new
                    {
                        Slot = slot,
                        Candidates = slotCandidates,
                        EmployeeCount = employeeCount
                    };
                })
                .OrderBy(item => item.EmployeeCount - demand[item.Slot])
                .ThenBy(item => item.Candidates.Count)
                .ThenBy(item => item.Slot)
                .Take(12)
                .ToList();

            foreach (var detail in detailedSlots)
            {
                var (date, hour) = GetBusinessDateAndHour(weekStart, detail.Slot);
                var employeeOptions = detail.Candidates
                    .GroupBy(candidate => candidate.EmployeeIndex)
                    .OrderBy(group => group.Key)
                    .Select(group =>
                        $"{GetEmployeeName(employees[group.Key])}: {group.Count()} option(s)")
                    .Take(8);
                var examples = FormatCandidateExamples(detail.Candidates, employees);

                diagnostics.Add(
                    $"{date:yyyy-MM-dd} {hour:00}:00: requires {demand[detail.Slot]} driver(s); " +
                    $"{detail.EmployeeCount} employee(s) and {detail.Candidates.Count} candidate shift(s) can cover this hour, " +
                    "but those shifts conflict with requirements in other hours. " +
                    $"Options by employee: {string.Join(", ", employeeOptions)}. " +
                    $"Candidate shifts: {examples}.");
            }
        }

        var omittedCount = slotsToExplain.Count(slot =>
            demand[slot] > 0 &&
            (blockedSlots.Contains(slot) || candidates.Count(candidate => candidate.CoveredSlots.Contains(slot)) == 0));
        var suffix = omittedCount > diagnostics.Count
            ? $"\nAdditional affected demand periods: {omittedCount - diagnostics.Count}."
            : string.Empty;

        return "No exact roster could be generated. Affected demand periods:\n" +
            string.Join("\n", diagnostics.Select(item => $"- {item}")) +
            suffix;
    }

    private static string FormatCandidateExamples(
        IReadOnlyList<CandidateShift> candidates,
        IReadOnlyList<Employee> employees)
    {
        return string.Join(
            "; ",
            candidates
                .OrderByDescending(candidate => candidate.DurationHours)
                .ThenBy(candidate => candidate.StartAbsoluteMinutes)
                .Select(candidate =>
                    $"{GetEmployeeName(employees[candidate.EmployeeIndex])} " +
                    $"{candidate.Date:dd MMM} {candidate.StartTime:HH:mm}–{candidate.FinishTime:HH:mm}")
                .Distinct()
                .Take(8));
    }

    private static string FormatAvailabilityExamples(
        DateOnly date,
        IReadOnlyList<Shift> availability,
        IReadOnlyList<Employee> employees)
    {
        var employeesById = employees.ToDictionary(employee => employee.Id);

        return string.Join(
            "; ",
            availability
                .Where(shift => shift.Date == date && employeesById.ContainsKey(shift.EmployeeId))
                .OrderBy(shift => shift.StartTime)
                .Select(shift =>
                    $"{GetEmployeeName(employeesById[shift.EmployeeId])} " +
                    $"{shift.StartTime:HH:mm}–{shift.FinishTime:HH:mm}")
                .Distinct()
                .Take(8));
    }

    private static (DateOnly Date, int Hour) GetBusinessDateAndHour(DateOnly weekStart, int slot)
    {
        var businessDayIndex = slot / 24;
        var slotHour = slot % 24;
        var hour = slotHour < 18 ? slotHour + 6 : slotHour - 18;
        return (weekStart.AddDays(businessDayIndex), hour);
    }

    private static string GetEmployeeName(Employee employee) =>
        $"{employee.FirstName} {employee.LastName}".Trim();

    private static int GetDemandSlot(int columnPosition, int hour)
    {
        return columnPosition * 24 + (hour >= 6 ? hour - 6 : 18 + hour);
    }

    private static int? GetDemandSlotForActualHour(int actualHour)
    {
        if (actualHour < 0)
        {
            return null;
        }

        var dayIndex = actualHour / 24;
        var hour = actualHour % 24;
        var businessDayIndex = hour >= 6 ? dayIndex : dayIndex - 1;

        if (businessDayIndex is < 0 or >= 7)
        {
            return null;
        }

        return GetDemandSlot(businessDayIndex, hour);
    }

    private bool IsShopOpen(DateOnly weekStart, int slot)
    {
        var businessDayIndex = slot / 24;
        var slotHour = slot % 24;
        var hour = slotHour < 18 ? slotHour + 6 : slotHour - 18;
        var date = weekStart.AddDays(businessDayIndex);
        var hours = shopHours.For(date.DayOfWeek);
        var openingMinutes = ToMinutes(hours.OpeningTime);
        var closingMinutes = ToMinutes(hours.ClosingTime);
        var currentMinutes = hour * 60;

        if (closingMinutes <= openingMinutes)
        {
            closingMinutes += 24 * 60;
        }

        if (currentMinutes < openingMinutes)
        {
            currentMinutes += 24 * 60;
        }

        return currentMinutes >= openingMinutes && currentMinutes < closingMinutes;
    }

    private static int ToMinutes(TimeOnly time) => time.Hour * 60 + time.Minute;

    private sealed record CandidateShift(
        Guid EmployeeId,
        int EmployeeIndex,
        DateOnly Date,
        TimeOnly StartTime,
        TimeOnly FinishTime,
        int DurationHours,
        int StartAbsoluteMinutes,
        int EndAbsoluteMinutes,
        int[] CoveredSlots);

    private sealed class CpSatRosterSolver(
        IReadOnlyList<CandidateShift> candidates,
        IReadOnlyList<int> demand,
        IReadOnlyList<Employee> employees,
        RosterGenerationParameters parameters)
    {
        private readonly List<int>[] candidatesBySlot = BuildCandidatesBySlot(candidates);
        private readonly List<int>[] candidatesByEmployee = BuildCandidatesByEmployee(candidates, employees.Count);

        public IReadOnlyList<int> FailureSlots { get; private set; } = [];

        public async Task<IReadOnlyList<CandidateShift>?> SolveAsync(
            CancellationToken cancellationToken,
            Func<string, int, string, Task>? reportProgress)
        {
            var model = new CpModel();
            var selected = candidates
                .Select((_, index) => model.NewBoolVar($"shift_{index}"))
                .ToArray();

            await ReportAsync(
                reportProgress,
                "constraint-optimization",
                40,
                $"Built {selected.Length} binary shift decisions and {demand.Count} hourly demand slots.");

            AddDemandConstraints(model, selected);
            AddEmployeeConstraints(model, selected);
            AddConflictConstraints(model, selected);
            AddObjective(model, selected);

            var solver = new CpSolver
            {
                StringParameters = $"num_search_workers: {Math.Clamp(Environment.ProcessorCount, 1, 8)} log_search_progress: false"
            };
            using var cancellationRegistration = cancellationToken.Register(solver.StopSearch);
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var solveTask = Task.Run(() => solver.Solve(model), CancellationToken.None);

            try
            {
                while (!solveTask.IsCompleted)
                {
                    var delayTask = Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
                    var completedTask = await Task.WhenAny(solveTask, delayTask);

                    if (completedTask == solveTask)
                    {
                        break;
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    await ReportAsync(
                        reportProgress,
                        "constraint-optimization",
                        Math.Min(85, 45 + (int)Math.Min(40, stopwatch.Elapsed.TotalSeconds)),
                        $"CP-SAT is searching for an exact roster ({stopwatch.Elapsed.TotalSeconds:0.0}s elapsed).");
                }

                var status = await solveTask;
                cancellationToken.ThrowIfCancellationRequested();

                if (status is CpSolverStatus.Optimal or CpSolverStatus.Feasible)
                {
                    await ReportAsync(
                        reportProgress,
                        "constraint-optimization",
                        88,
                        $"CP-SAT found a roster in {stopwatch.Elapsed.TotalSeconds:0.0}s.");

                    return Enumerable
                        .Range(0, candidates.Count)
                        .Where(index => solver.BooleanValue(selected[index]))
                        .Select(index => candidates[index])
                        .ToList();
                }

                if (status == CpSolverStatus.Infeasible)
                {
                    FailureSlots = Enumerable
                        .Range(0, demand.Count)
                        .Where(slot => demand[slot] > 0 && candidatesBySlot[slot].Count < demand[slot])
                        .ToArray();

                    await ReportAsync(
                        reportProgress,
                        "constraint-optimization",
                        88,
                        "CP-SAT proved that the current hard constraints cannot all be satisfied.");
                    return null;
                }

                throw new RosterGenerationException(
                    $"The constraint solver stopped with status {status} before finding a valid roster.");
            }
            catch
            {
                solver.StopSearch();
                await solveTask;
                throw;
            }
        }

        private void AddDemandConstraints(CpModel model, IReadOnlyList<IntVar> selected)
        {
            for (var slot = 0; slot < demand.Count; slot++)
            {
                var slotCandidates = candidatesBySlot[slot];

                if (slotCandidates.Count == 0)
                {
                    if (demand[slot] > 0)
                    {
                        FailureSlots = [slot];
                        model.Add(LinearExpr.Constant(0) == demand[slot]);
                    }

                    continue;
                }

                model.Add(
                    LinearExpr.Sum(slotCandidates.Select(index => (LinearExpr)selected[index])) ==
                    demand[slot]);
            }
        }

        private void AddEmployeeConstraints(CpModel model, IReadOnlyList<IntVar> selected)
        {
            for (var employeeIndex = 0; employeeIndex < employees.Count; employeeIndex++)
            {
                var employeeCandidates = candidatesByEmployee[employeeIndex];
                var hours = model.NewIntVar(0, 168, $"employee_{employeeIndex}_hours");
                var hourTerms = employeeCandidates
                    .Select(index => LinearExpr.Term(selected[index], candidates[index].DurationHours))
                    .ToArray();

                model.Add(hours == LinearExpr.Sum(hourTerms));
                model.Add(hours >= MinimumShiftHours);
            }
        }

        private void AddConflictConstraints(CpModel model, IReadOnlyList<IntVar> selected)
        {
            for (var employeeIndex = 0; employeeIndex < candidatesByEmployee.Length; employeeIndex++)
            {
                var employeeCandidates = candidatesByEmployee[employeeIndex];

                for (var first = 0; first < employeeCandidates.Count; first++)
                {
                    for (var second = first + 1; second < employeeCandidates.Count; second++)
                    {
                        var firstIndex = employeeCandidates[first];
                        var secondIndex = employeeCandidates[second];

                        if (Conflicts(candidates[firstIndex], candidates[secondIndex]))
                        {
                            model.Add(selected[firstIndex] + selected[secondIndex] <= 1);
                        }
                    }
                }
            }
        }

        private void AddObjective(CpModel model, IReadOnlyList<IntVar> selected)
        {
            var objectiveVariables = new List<LinearExpr>();
            var objectiveCoefficients = new List<long>();

            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index];
                var coefficient = GetShiftPenalty(candidate);

                if (coefficient != 0)
                {
                    objectiveVariables.Add(selected[index]);
                    objectiveCoefficients.Add(coefficient);
                }
            }

            for (var employeeIndex = 0; employeeIndex < employees.Count; employeeIndex++)
            {
                var employee = employees[employeeIndex];
                var employeeCandidates = candidatesByEmployee[employeeIndex];
                var hours = model.NewIntVar(0, 168, $"employee_{employeeIndex}_objective_hours");
                var deviation = model.NewIntVar(0, 168, $"employee_{employeeIndex}_target_deviation");
                var hourTerms = employeeCandidates
                    .Select(index => LinearExpr.Term(selected[index], candidates[index].DurationHours))
                    .ToArray();

                model.Add(hours == LinearExpr.Sum(hourTerms));
                model.AddAbsEquality(deviation, hours - employee.TargetHours);

                if (parameters.TargetHoursWeight != 0)
                {
                    objectiveVariables.Add(deviation);
                    objectiveCoefficients.Add(parameters.TargetHoursWeight);
                }
            }

            if (objectiveVariables.Count > 0)
            {
                model.Minimize(LinearExpr.WeightedSum(objectiveVariables, objectiveCoefficients));
            }
        }

        private long GetShiftPenalty(CandidateShift candidate)
        {
            var penalty = candidate.DurationHours < PreferredMinimumShiftHours
                ? parameters.ShortShiftPenalty
                : 0;
            var bonus = candidate.DurationHours >= PreferredMinimumShiftHours
                ? parameters.LongShiftBonus
                : 0;

            return penalty - bonus + GetUnpleasantHoursPenalty(candidate);
        }

        private int GetUnpleasantHoursPenalty(CandidateShift candidate)
        {
            var startHour = candidate.StartTime.Hour;
            var finishHour = candidate.FinishTime.Hour;
            var lateFinish = finishHour <= 6
                ? Math.Max(0, finishHour + 24 - 22)
                : Math.Max(0, finishHour - 22);
            var earlyStart = Math.Max(0, 8 - startHour);

            return lateFinish * parameters.LateFinishPenalty +
                   earlyStart * parameters.EarlyStartPenalty;
        }

        private static bool Conflicts(CandidateShift first, CandidateShift second)
        {
            var gap = first.StartAbsoluteMinutes >= second.EndAbsoluteMinutes
                ? first.StartAbsoluteMinutes - second.EndAbsoluteMinutes
                : second.StartAbsoluteMinutes - first.EndAbsoluteMinutes;

            return gap < 0 || gap < MinimumBreakMinutes;
        }

        private static List<int>[] BuildCandidatesBySlot(IReadOnlyList<CandidateShift> candidates)
        {
            var result = Enumerable
                .Range(0, HoursInWeek)
                .Select(_ => new List<int>())
                .ToArray();

            for (var index = 0; index < candidates.Count; index++)
            {
                foreach (var slot in candidates[index].CoveredSlots)
                {
                    result[slot].Add(index);
                }
            }

            return result;
        }

        private static List<int>[] BuildCandidatesByEmployee(
            IReadOnlyList<CandidateShift> candidates,
            int employeeCount)
        {
            var result = Enumerable
                .Range(0, employeeCount)
                .Select(_ => new List<int>())
                .ToArray();

            for (var index = 0; index < candidates.Count; index++)
            {
                result[candidates[index].EmployeeIndex].Add(index);
            }

            return result;
        }

        private static Task ReportAsync(
            Func<string, int, string, Task>? reportProgress,
            string stage,
            int progress,
            string message) =>
            reportProgress is null
                ? Task.CompletedTask
                : reportProgress(stage, progress, message);
    }

    private sealed class GeneticRosterSolver(
        IReadOnlyList<CandidateShift> candidates,
        IReadOnlyList<int> demand,
        IReadOnlyList<Employee> employees,
        RosterGenerationParameters parameters,
        CancellationToken cancellationToken)
    {
        private readonly Random random = new(20260907);
        private readonly CancellationToken jobCancellationToken = cancellationToken;
        private readonly List<int>[] candidatesBySlot = BuildCandidatesBySlot(candidates, cancellationToken);
        private readonly HashSet<int> failureSlots = [];

        public IReadOnlyList<int> FailureSlots => failureSlots.ToArray();

        public async Task<IReadOnlyList<CandidateShift>?> SolveAsync(
            CancellationToken cancellationToken,
            Func<string, int, string, Task>? reportProgress)
        {
            var population = Enumerable.Range(0, parameters.PopulationSize)
                .Select(_ => CreateIndividual())
                .ToList();
            // Report every generation so every admin sees a detailed live trace.
            var progressInterval = 1;

            for (var generation = 0; generation < parameters.GenerationCount; generation++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (generation % progressInterval == 0)
                {
                    await ReportAsync(
                        reportProgress,
                        "genetic-optimization",
                        40 + (generation * 35 / parameters.GenerationCount),
                        $"Genetic optimization: generation {generation + 1} of {parameters.GenerationCount}.");
                }

                population = population
                    .OrderBy(individual => individual.Score)
                    .ToList();

                if (population[0].IsComplete)
                {
                    return population[0].Selected.Select(index => candidates[index]).ToList();
                }

                var nextPopulation = population.Take(parameters.EliteCount).ToList();

                while (nextPopulation.Count < parameters.PopulationSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var first = Tournament(population);
                    var second = Tournament(population);
                    var childPriorities = Crossover(first.Priorities, second.Priorities);
                    Mutate(childPriorities);
                    nextPopulation.Add(Evaluate(childPriorities));
                }

                population = nextPopulation;
            }

            var best = population
                .OrderBy(individual => individual.Score)
                .First();

            if (best.IsComplete)
            {
                return best.Selected.Select(index => candidates[index]).ToList();
            }

            await ReportAsync(
                reportProgress,
                "exact-search",
                78,
                "Genetic optimization did not find a complete roster; starting exact constraint search.");
            var exactSelection = await TryExactSearchAsync(best.Priorities, cancellationToken, reportProgress);
            return exactSelection?.Select(index => candidates[index]).ToList();
        }

        private static Task ReportAsync(
            Func<string, int, string, Task>? reportProgress,
            string stage,
            int progress,
            string message) =>
            reportProgress is null
                ? Task.CompletedTask
                : reportProgress(stage, progress, message);

        private Individual CreateIndividual()
        {
            jobCancellationToken.ThrowIfCancellationRequested();
            var priorities = candidates.Select(_ => random.NextDouble()).ToArray();
            return Evaluate(priorities);
        }

        private Individual Evaluate(double[] priorities)
        {
            jobCancellationToken.ThrowIfCancellationRequested();
            var remaining = demand.ToArray();
            var selected = BuildGreedySelection(priorities, remaining, out var employeeHours);
            var uncoveredHours = remaining.Sum();
            var minimumHoursMissing = employeeHours.Sum(hours => Math.Max(0, MinimumShiftHours - hours));
            var soloHours = GetSoloHours(selected);
            var targetDeviation = employeeHours
                .Select((hours, index) => Math.Abs(hours - employees[index].TargetHours))
                .Sum();
            var shortShiftPenalty = selected
                .Select(index => candidates[index])
                .Count(candidate => candidate.DurationHours < PreferredMinimumShiftHours);

            var score = uncoveredHours * 1_000_000L
                + soloHours * 500_000L
                + minimumHoursMissing * 100_000L
                + targetDeviation * parameters.TargetHoursWeight
                + shortShiftPenalty * parameters.ShortShiftPenalty
                + selected.Sum(index => GetUnpleasantHoursPenalty(candidates[index]));

            return new Individual(
                priorities,
                selected,
                score,
                uncoveredHours == 0 && minimumHoursMissing == 0 && soloHours == 0);
        }

        private int GetSoloHours(IReadOnlyList<int> selected)
        {
            var employeeIndexesBySlot = new Dictionary<int, HashSet<int>>();

            foreach (var selectedIndex in selected)
            {
                jobCancellationToken.ThrowIfCancellationRequested();
                var candidate = candidates[selectedIndex];

                foreach (var slot in candidate.CoveredSlots)
                {
                    if (!employeeIndexesBySlot.TryGetValue(slot, out var employeeIndexes))
                    {
                        employeeIndexes = [];
                        employeeIndexesBySlot[slot] = employeeIndexes;
                    }

                    employeeIndexes.Add(candidate.EmployeeIndex);
                }
            }

            return selected
                .Select(index => candidates[index])
                .Where(candidate => !employees[candidate.EmployeeIndex].CanWorkAlone)
                .Sum(candidate => candidate.CoveredSlots.Count(slot =>
                    employeeIndexesBySlot[slot].Any(employeeIndex =>
                        employeeIndex != candidate.EmployeeIndex)));
        }

        private List<int> BuildGreedySelection(
            IReadOnlyList<double> priorities,
            int[] remaining,
            out int[] employeeHours)
        {
            employeeHours = new int[employees.Count];
            var selected = new List<int>();

            while (remaining.Sum() > 0)
            {
                jobCancellationToken.ThrowIfCancellationRequested();
                var bestIndex = -1;
                var bestScore = double.MinValue;

                for (var index = 0; index < candidates.Count; index++)
                {
                    jobCancellationToken.ThrowIfCancellationRequested();
                    var candidate = candidates[index];

                    if (!CanAdd(candidate, selected) ||
                        candidate.CoveredSlots.Any(slot => remaining[slot] <= 0))
                    {
                        continue;
                    }

                    var gain = candidate.CoveredSlots.Length;
                    var targetDifference = Math.Abs(
                        employeeHours[candidate.EmployeeIndex] + candidate.DurationHours -
                        employees[candidate.EmployeeIndex].TargetHours);
                    var longShiftBonus = candidate.DurationHours >= PreferredMinimumShiftHours
                        ? parameters.LongShiftBonus
                        : 0;
                    var score = gain * 10_000D
                        + longShiftBonus
                        - targetDifference * parameters.TargetHoursWeight
                        - GetUnpleasantHoursPenalty(candidate)
                        + priorities[index];

                    if (score > bestScore)
                    {
                        bestIndex = index;
                        bestScore = score;
                    }
                }

                if (bestIndex < 0)
                {
                    break;
                }

                var selectedCandidate = candidates[bestIndex];
                selected.Add(bestIndex);
                employeeHours[selectedCandidate.EmployeeIndex] += selectedCandidate.DurationHours;

                foreach (var slot in selectedCandidate.CoveredSlots)
                {
                    remaining[slot]--;
                }
            }

            return selected;
        }

        private async Task<IReadOnlyList<int>?> TryExactSearchAsync(
            IReadOnlyList<double> priorities,
            CancellationToken cancellationToken,
            Func<string, int, string, Task>? reportProgress)
        {
            var remaining = demand.ToArray();
            var employeeHours = new int[employees.Count];
            var selected = new List<int>();
            var nodes = 0;
            var progressInterval = Math.Max(1, parameters.ExactSearchNodeLimit / 20);

            return await SearchAsync() ? selected.ToList() : null;

            async Task<bool> SearchAsync()
            {
                cancellationToken.ThrowIfCancellationRequested();
                nodes++;

                if (nodes > parameters.ExactSearchNodeLimit)
                {
                    return false;
                }

                if (nodes % progressInterval == 0)
                {
                    await ReportAsync(
                        reportProgress,
                        "exact-search",
                        78 + Math.Min(12, nodes * 12 / parameters.ExactSearchNodeLimit),
                        $"Exact constraint search: checked {nodes:n0} combinations.");
                }

                var nextSlot = FindMostConstrainedSlot();

                if (nextSlot < 0)
                {
                    return employeeHours.All(hours => hours >= MinimumShiftHours) &&
                        GetSoloHours(selected) == 0;
                }

                var options = candidatesBySlot[nextSlot]
                    .Where(index =>
                    {
                        var candidate = candidates[index];
                        return CanAdd(candidate, selected) &&
                            candidate.CoveredSlots.All(slot => remaining[slot] > 0);
                    })
                    .OrderByDescending(index => candidates[index].DurationHours >= PreferredMinimumShiftHours
                        ? parameters.LongShiftBonus
                        : 0)
                    .ThenBy(index => Math.Abs(
                        employeeHours[candidates[index].EmployeeIndex] + candidates[index].DurationHours -
                        employees[candidates[index].EmployeeIndex].TargetHours) * parameters.TargetHoursWeight
                        + (candidates[index].DurationHours < PreferredMinimumShiftHours
                            ? parameters.ShortShiftPenalty
                            : 0)
                        + GetUnpleasantHoursPenalty(candidates[index]))
                    .ThenByDescending(index => priorities[index])
                    .ToList();

                if (options.Count == 0)
                {
                    failureSlots.Add(nextSlot);
                }

                foreach (var index in options)
                {
                    var candidate = candidates[index];
                    selected.Add(index);
                    employeeHours[candidate.EmployeeIndex] += candidate.DurationHours;

                    foreach (var slot in candidate.CoveredSlots)
                    {
                        remaining[slot]--;
                    }

                    if (await SearchAsync())
                    {
                        return true;
                    }

                    foreach (var slot in candidate.CoveredSlots)
                    {
                        remaining[slot]++;
                    }

                    employeeHours[candidate.EmployeeIndex] -= candidate.DurationHours;
                    selected.RemoveAt(selected.Count - 1);
                }

                return false;
            }

            int FindMostConstrainedSlot()
            {
                var bestSlot = -1;
                var bestOptionCount = int.MaxValue;

                for (var slot = 0; slot < remaining.Length; slot++)
                {
                    if (remaining[slot] <= 0)
                    {
                        continue;
                    }

                    var optionCount = candidatesBySlot[slot].Count(index =>
                    {
                        var candidate = candidates[index];
                        return CanAdd(candidate, selected) &&
                            candidate.CoveredSlots.All(coveredSlot => remaining[coveredSlot] > 0);
                    });

                    if (optionCount < bestOptionCount)
                    {
                        bestSlot = slot;
                        bestOptionCount = optionCount;
                    }
                }

                return bestSlot;
            }
        }

        private bool CanAdd(CandidateShift candidate, IReadOnlyList<int> selected)
        {
            foreach (var selectedIndex in selected)
            {
                var other = candidates[selectedIndex];

                if (other.EmployeeIndex != candidate.EmployeeIndex)
                {
                    continue;
                }

                var gap = candidate.StartAbsoluteMinutes >= other.EndAbsoluteMinutes
                    ? candidate.StartAbsoluteMinutes - other.EndAbsoluteMinutes
                    : other.StartAbsoluteMinutes - candidate.EndAbsoluteMinutes;

                if (gap < 0 || gap < MinimumBreakMinutes)
                {
                    return false;
                }
            }

            return true;
        }

        private static List<int>[] BuildCandidatesBySlot(
            IReadOnlyList<CandidateShift> candidates,
            CancellationToken cancellationToken)
        {
            var result = Enumerable.Range(0, HoursInWeek)
                .Select(_ => new List<int>())
                .ToArray();

            for (var index = 0; index < candidates.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var slot in candidates[index].CoveredSlots)
                {
                    result[slot].Add(index);
                }
            }

            return result;
        }

        private Individual Tournament(IReadOnlyList<Individual> population)
        {
            var best = population[random.Next(population.Count)];

            for (var index = 1; index < parameters.TournamentSize; index++)
            {
                var contender = population[random.Next(population.Count)];

                if (contender.Score < best.Score)
                {
                    best = contender;
                }
            }

            return best;
        }

        private double[] Crossover(IReadOnlyList<double> first, IReadOnlyList<double> second)
        {
            var child = new double[first.Count];

            for (var index = 0; index < child.Length; index++)
            {
                jobCancellationToken.ThrowIfCancellationRequested();
                child[index] = random.NextDouble() < 0.5 ? first[index] : second[index];
            }

            return child;
        }

        private void Mutate(double[] priorities)
        {
            for (var index = 0; index < priorities.Length; index++)
            {
                jobCancellationToken.ThrowIfCancellationRequested();
                if (random.NextDouble() < (double)parameters.MutationRate)
                {
                    priorities[index] = random.NextDouble();
                }
            }
        }

        private int GetUnpleasantHoursPenalty(CandidateShift candidate)
        {
            var startHour = candidate.StartTime.Hour;
            var finishHour = candidate.FinishTime.Hour;
            var lateFinish = finishHour <= 6
                ? Math.Max(0, finishHour + 24 - 22)
                : Math.Max(0, finishHour - 22);
            var earlyStart = Math.Max(0, 8 - startHour);

            return lateFinish * parameters.LateFinishPenalty + earlyStart * parameters.EarlyStartPenalty;
        }

        private sealed record Individual(
            double[] Priorities,
            IReadOnlyList<int> Selected,
            long Score,
            bool IsComplete);
    }
}
