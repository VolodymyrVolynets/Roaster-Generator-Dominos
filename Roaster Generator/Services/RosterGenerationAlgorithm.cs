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
    private const int HoursInDay = 24;
    private const int DaysInWeek = 7;
    private const int HoursInWeek = DaysInWeek * HoursInDay;
    private const long HardConstraintPenalty = 1_000_000_000L;
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
            .Where(shift => shift.Date >= weekStart && shift.Date < weekStart.AddDays(DaysInWeek))
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
            $"Generated all {candidates.Count} valid candidate shifts from {availability.Count} availability entries.");

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
            "daily-options",
            35,
            "Building complete daily schedules from the full candidate-shift set.");

        var solver = new EvolutionaryRosterSolver(
            weekStart,
            candidates,
            demand,
            employees,
            parameters,
            cancellationToken);
        var selectedCandidates = await solver.SolveAsync(
            cancellationToken,
            async (stage, progress, message) => await ReportAsync(stage, progress, message));

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

        await ReportAsync("validation", 92, "Validating demand coverage and employee constraints.");
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

        for (var position = 0; position < DaysInWeek; position++)
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

            foreach (var column in columns.Values.Where(column => column.Position is >= 0 and < DaysInWeek))
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

            if (dayIndex is < 0 or >= DaysInWeek)
            {
                continue;
            }

            var availabilityStart = dayIndex * HoursInDay + availabilityShift.StartTime.Hour;
            var availabilityFinishHour = availabilityShift.FinishTime.Hour;

            if (availabilityFinishHour <= availabilityShift.StartTime.Hour)
            {
                availabilityFinishHour += HoursInDay;
            }

            var availabilityEnd = dayIndex * HoursInDay + availabilityFinishHour;

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
                        weekStart.AddDays(start / HoursInDay),
                        TimeOnly.FromTimeSpan(TimeSpan.FromHours(start % HoursInDay)),
                        TimeOnly.FromTimeSpan(TimeSpan.FromHours(end % HoursInDay)),
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
                    ? "the daily/weekly shift combinations conflict with another required period or the 7-hour employee break"
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
                "Evolutionary search found complete daily demand coverage, but no weekly combination satisfies shift-overlap and minimum-hours constraints. Short breaks are scored as a penalty, so review the break deficit shown in the generation log.");

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
        var businessDayIndex = slot / HoursInDay;
        var slotHour = slot % HoursInDay;
        var hour = slotHour < 18 ? slotHour + 6 : slotHour - 18;
        return (weekStart.AddDays(businessDayIndex), hour);
    }

    private static string GetEmployeeName(Employee employee) =>
        $"{employee.FirstName} {employee.LastName}".Trim();

    private static int GetDemandSlot(int columnPosition, int hour) =>
        columnPosition * HoursInDay + (hour >= 6 ? hour - 6 : 18 + hour);

    private static int? GetDemandSlotForActualHour(int actualHour)
    {
        if (actualHour < 0)
        {
            return null;
        }

        var dayIndex = actualHour / HoursInDay;
        var hour = actualHour % HoursInDay;
        var businessDayIndex = hour >= 6 ? dayIndex : dayIndex - 1;

        if (businessDayIndex is < 0 or >= DaysInWeek)
        {
            return null;
        }

        return GetDemandSlot(businessDayIndex, hour);
    }

    private bool IsShopOpen(DateOnly weekStart, int slot)
    {
        var businessDayIndex = slot / HoursInDay;
        var slotHour = slot % HoursInDay;
        var hour = slotHour < 18 ? slotHour + 6 : slotHour - 18;
        var date = weekStart.AddDays(businessDayIndex);
        var hours = shopHours.For(date.DayOfWeek);
        var openingMinutes = ToMinutes(hours.OpeningTime);
        var closingMinutes = ToMinutes(hours.ClosingTime);
        var currentMinutes = hour * 60;

        if (closingMinutes <= openingMinutes)
        {
            closingMinutes += HoursInDay * 60;
        }

        if (currentMinutes < openingMinutes)
        {
            currentMinutes += HoursInDay * 60;
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

    private sealed record DailySchedule(IReadOnlyList<int> CandidateIndexes);

    private sealed class EvolutionaryRosterSolver(
        DateOnly weekStart,
        IReadOnlyList<CandidateShift> candidates,
        IReadOnlyList<int> demand,
        IReadOnlyList<Employee> employees,
        RosterGenerationParameters parameters,
        CancellationToken jobCancellationToken)
    {
        private readonly Random random = new(unchecked(20260907 + weekStart.DayNumber));
        private readonly List<DailySchedule>[] dailyOptions = new List<DailySchedule>[DaysInWeek];
        private readonly HashSet<int> failureSlots = [];

        public IReadOnlyList<int> FailureSlots => failureSlots.ToArray();

        public async Task<IReadOnlyList<CandidateShift>?> SolveAsync(
            CancellationToken cancellationToken,
            Func<string, int, string, Task>? reportProgress)
        {
            for (var dayIndex = 0; dayIndex < DaysInWeek; dayIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var date = weekStart.AddDays(dayIndex);
                var dayCandidateIndexes = candidates
                    .Select((candidate, index) => (candidate, index))
                    .Where(item => item.candidate.Date == date)
                    .Where(item => item.candidate.CoveredSlots.All(slot => slot / HoursInDay == dayIndex))
                    .Select(item => item.index)
                    .ToList();

                dailyOptions[dayIndex] = BuildDailyOptions(
                    dayIndex,
                    dayCandidateIndexes,
                    cancellationToken);

                await ReportAsync(
                    reportProgress,
                    "daily-options",
                    36 + ((dayIndex + 1) * 8 / DaysInWeek),
                    $"{date:dddd}: generated {dayCandidateIndexes.Count} candidate shifts and {dailyOptions[dayIndex].Count} complete daily schedule option(s).");

                if (dailyOptions[dayIndex].Count == 0)
                {
                    for (var slot = dayIndex * HoursInDay; slot < (dayIndex + 1) * HoursInDay; slot++)
                    {
                        if (demand[slot] > 0)
                        {
                            failureSlots.Add(slot);
                        }
                    }

                    await ReportAsync(
                        reportProgress,
                        "daily-options",
                        44,
                        $"{date:dddd} has no complete daily schedule that exactly covers its demand.");
                    return null;
                }
            }

            await ReportAsync(
                reportProgress,
                "evolutionary-optimization",
                45,
                $"Starting day-preserving evolution with {parameters.PopulationSize} individual(s) and {parameters.GenerationCount} generation(s).");

            var population = CreateInitialPopulation(cancellationToken);

            for (var generation = 0; generation < parameters.GenerationCount; generation++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                population = population
                    .OrderBy(individual => individual.Fitness.Score)
                    .ToList();

                var best = population[0];
                var progress = 45 + ((generation + 1) * 43 / Math.Max(1, parameters.GenerationCount));
                await ReportAsync(
                    reportProgress,
                    "evolutionary-optimization",
                    Math.Min(88, progress),
                    $"Evolutionary optimization: generation {generation + 1} of {parameters.GenerationCount}; " +
                    $"best score {best.Fitness.Score:n0}, break deficit {best.Fitness.BreakViolationMinutes} minute(s), " +
                    $"minimum-hours deficit {best.Fitness.MinimumHoursMissing} hour(s), " +
                    $"{best.Fitness.TotalShiftCount} shift(s) across the week.");

                if (best.Fitness.IsComplete)
                {
                    await ReportAsync(
                        reportProgress,
                        "evolutionary-optimization",
                        90,
                        $"Evolutionary optimization found a complete roster in generation {generation + 1}.");
                    return SelectCandidates(best);
                }

                var nextPopulation = population
                    .Take(Math.Min(parameters.EliteCount, population.Count))
                    .ToList();

                while (nextPopulation.Count < parameters.PopulationSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var first = Tournament(population);
                    var second = Tournament(population);
                    var childGenes = Crossover(first.DayOptionIndexes, second.DayOptionIndexes);
                    Mutate(childGenes);
                    nextPopulation.Add(Evaluate(childGenes));
                }

                population = nextPopulation;
            }

            var finalBest = population
                .OrderBy(individual => individual.Fitness.Score)
                .First();
            AddFailureSlotsForBest(finalBest);

            await ReportAsync(
                reportProgress,
                "evolutionary-optimization",
                90,
                $"Evolutionary optimization finished without a complete roster. Best score {finalBest.Fitness.Score:n0}; " +
                $"break deficit {finalBest.Fitness.BreakViolationMinutes} minute(s), " +
                $"minimum-hours deficit {finalBest.Fitness.MinimumHoursMissing} hour(s).");
            return null;
        }

        private List<DailySchedule> BuildDailyOptions(
            int dayIndex,
            IReadOnlyList<int> dayCandidateIndexes,
            CancellationToken cancellationToken)
        {
            var dayStartSlot = dayIndex * HoursInDay;
            var remaining = demand
                .Skip(dayStartSlot)
                .Take(HoursInDay)
                .ToArray();
            var options = new List<DailySchedule>();

            if (remaining.Sum() == 0)
            {
                return [new DailySchedule([])];
            }

            var candidatesByLocalSlot = Enumerable
                .Range(0, HoursInDay)
                .Select(_ => new List<int>())
                .ToArray();

            foreach (var candidateIndex in dayCandidateIndexes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var slot in candidates[candidateIndex].CoveredSlots)
                {
                    var localSlot = slot - dayStartSlot;

                    if (localSlot is >= 0 and < HoursInDay)
                    {
                        candidatesByLocalSlot[localSlot].Add(candidateIndex);
                    }
                }
            }

            var selected = new List<int>();
            var selectedSet = new HashSet<int>();
            var seenOptions = new HashSet<string>(StringComparer.Ordinal);

            Search();
            return options;

            void Search()
            {
                cancellationToken.ThrowIfCancellationRequested();
                var nextSlot = FindMostConstrainedSlot();

                if (nextSlot < 0)
                {
                    var indexes = selected.Order().ToArray();
                    var key = string.Join(',', indexes);

                    if (seenOptions.Add(key))
                    {
                        options.Add(new DailySchedule(indexes));
                    }

                    return;
                }

                var candidateIndexes = candidatesByLocalSlot[nextSlot]
                    .Where(index => CanPlace(index, selected, selectedSet, remaining, dayStartSlot))
                    .OrderByDescending(index => candidates[index].DurationHours)
                    .ThenBy(index => index)
                    .ToList();

                foreach (var candidateIndex in candidateIndexes)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var candidate = candidates[candidateIndex];
                    selected.Add(candidateIndex);
                    selectedSet.Add(candidateIndex);

                    foreach (var slot in candidate.CoveredSlots)
                    {
                        remaining[slot - dayStartSlot]--;
                    }

                    Search();

                    foreach (var slot in candidate.CoveredSlots)
                    {
                        remaining[slot - dayStartSlot]++;
                    }

                    selectedSet.Remove(candidateIndex);
                    selected.RemoveAt(selected.Count - 1);
                }
            }

            int FindMostConstrainedSlot()
            {
                var bestSlot = -1;
                var bestOptionCount = int.MaxValue;

                for (var localSlot = 0; localSlot < remaining.Length; localSlot++)
                {
                    if (remaining[localSlot] <= 0)
                    {
                        continue;
                    }

                    var optionCount = candidatesByLocalSlot[localSlot]
                        .Count(index => CanPlace(index, selected, selectedSet, remaining, dayStartSlot));

                    if (optionCount < bestOptionCount)
                    {
                        bestSlot = localSlot;
                        bestOptionCount = optionCount;
                    }
                }

                return bestSlot;
            }
        }

        private bool CanPlace(
            int candidateIndex,
            IReadOnlyList<int> selected,
            IReadOnlySet<int> selectedSet,
            IReadOnlyList<int> remaining,
            int dayStartSlot)
        {
            if (selectedSet.Contains(candidateIndex))
            {
                return false;
            }

            var candidate = candidates[candidateIndex];

            if (candidate.CoveredSlots.Any(slot =>
                    slot / HoursInDay != dayStartSlot / HoursInDay ||
                    remaining[slot - dayStartSlot] <= 0))
            {
                return false;
            }

            return selected
                .Select(index => candidates[index])
                .Where(other => other.EmployeeIndex == candidate.EmployeeIndex)
                .All(other => !Overlaps(candidate, other));
        }

        private List<Individual> CreateInitialPopulation(CancellationToken cancellationToken)
        {
            var population = new List<Individual>(parameters.PopulationSize);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var attempts = 0;
            var maxAttempts = Math.Max(parameters.PopulationSize * 10, 100);

            while (population.Count < parameters.PopulationSize && attempts++ < maxAttempts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var genes = new int[DaysInWeek];

                for (var day = 0; day < DaysInWeek; day++)
                {
                    var optionCount = dailyOptions[day].Count;
                    genes[day] = population.Count < optionCount
                        ? (population.Count + day) % optionCount
                        : random.Next(optionCount);
                }

                var key = string.Join(',', genes);

                if (seen.Add(key) || population.Count == 0)
                {
                    population.Add(Evaluate(genes));
                }
            }

            while (population.Count < parameters.PopulationSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                population.Add(Evaluate(CreateRandomGenome()));
            }

            return population;
        }

        private int[] CreateRandomGenome()
        {
            var genes = new int[DaysInWeek];

            for (var day = 0; day < DaysInWeek; day++)
            {
                jobCancellationToken.ThrowIfCancellationRequested();
                genes[day] = random.Next(dailyOptions[day].Count);
            }

            return genes;
        }

        private Individual Evaluate(IReadOnlyList<int> dayOptionIndexes)
        {
            jobCancellationToken.ThrowIfCancellationRequested();
            var selected = new List<CandidateShift>();
            var employeeHours = new int[employees.Count];
            var totalShiftCount = 0;
            var longShiftCount = 0;
            var shortShiftCount = 0;

            for (var day = 0; day < DaysInWeek; day++)
            {
                var schedule = dailyOptions[day][dayOptionIndexes[day]];
                totalShiftCount += schedule.CandidateIndexes.Count;

                foreach (var candidateIndex in schedule.CandidateIndexes)
                {
                    var candidate = candidates[candidateIndex];
                    selected.Add(candidate);
                    employeeHours[candidate.EmployeeIndex] += candidate.DurationHours;

                    if (candidate.DurationHours >= PreferredMinimumShiftHours)
                    {
                        longShiftCount++;
                    }
                    else
                    {
                        shortShiftCount++;
                    }
                }
            }

            var overlapCount = 0;
            var breakViolationMinutes = 0;

            foreach (var employeeShifts in selected
                         .GroupBy(candidate => candidate.EmployeeIndex)
                         .Select(group => group.OrderBy(candidate => candidate.StartAbsoluteMinutes).ToList()))
            {
                for (var index = 1; index < employeeShifts.Count; index++)
                {
                    var previous = employeeShifts[index - 1];
                    var current = employeeShifts[index];
                    var gap = current.StartAbsoluteMinutes - previous.EndAbsoluteMinutes;

                    if (gap < 0)
                    {
                        overlapCount++;
                    }
                    else if (gap < MinimumBreakMinutes)
                    {
                        breakViolationMinutes += MinimumBreakMinutes - gap;
                    }
                }
            }

            var minimumHoursMissing = employeeHours
                .Sum(hours => Math.Max(0, MinimumShiftHours - hours));
            var targetDeviation = employeeHours
                .Select((hours, index) => Math.Abs(hours - employees[index].TargetHours))
                .Sum();
            var dailyShiftCountPenalty = totalShiftCount * parameters.DailyShiftCountPenalty;
            var score = overlapCount * HardConstraintPenalty +
                        minimumHoursMissing * HardConstraintPenalty +
                        breakViolationMinutes * parameters.ShortBreakPenalty +
                        targetDeviation * parameters.TargetHoursWeight +
                        dailyShiftCountPenalty +
                        shortShiftCount * parameters.ShortShiftPenalty -
                        longShiftCount * parameters.LongShiftBonus;

            return new Individual(
                dayOptionIndexes.ToArray(),
                new Fitness(
                    score,
                    overlapCount,
                    breakViolationMinutes,
                    minimumHoursMissing,
                    totalShiftCount,
                    overlapCount == 0 &&
                    minimumHoursMissing == 0));
        }

        private IReadOnlyList<CandidateShift> SelectCandidates(Individual individual)
        {
            return Enumerable
                .Range(0, DaysInWeek)
                .SelectMany(day => dailyOptions[day][individual.DayOptionIndexes[day]].CandidateIndexes)
                .Select(index => candidates[index])
                .ToList();
        }

        private void AddFailureSlotsForBest(Individual individual)
        {
            for (var day = 0; day < DaysInWeek; day++)
            {
                var schedule = dailyOptions[day][individual.DayOptionIndexes[day]];

                foreach (var candidateIndex in schedule.CandidateIndexes)
                {
                    foreach (var slot in candidates[candidateIndex].CoveredSlots)
                    {
                        failureSlots.Add(slot);
                    }
                }
            }

            if (failureSlots.Count == 0)
            {
                for (var slot = 0; slot < demand.Count; slot++)
                {
                    if (demand[slot] > 0)
                    {
                        failureSlots.Add(slot);
                    }
                }
            }
        }

        private Individual Tournament(IReadOnlyList<Individual> population)
        {
            var best = population[random.Next(population.Count)];

            for (var index = 1; index < parameters.TournamentSize; index++)
            {
                var contender = population[random.Next(population.Count)];

                if (contender.Fitness.Score < best.Fitness.Score)
                {
                    best = contender;
                }
            }

            return best;
        }

        private int[] Crossover(IReadOnlyList<int> first, IReadOnlyList<int> second)
        {
            var child = new int[DaysInWeek];

            for (var day = 0; day < DaysInWeek; day++)
            {
                jobCancellationToken.ThrowIfCancellationRequested();
                child[day] = random.NextDouble() < 0.5 ? first[day] : second[day];
            }

            return child;
        }

        private void Mutate(int[] dayOptionIndexes)
        {
            var mutated = false;

            for (var day = 0; day < DaysInWeek; day++)
            {
                jobCancellationToken.ThrowIfCancellationRequested();

                if (random.NextDouble() >= (double)parameters.MutationRate || dailyOptions[day].Count <= 1)
                {
                    continue;
                }

                var current = dayOptionIndexes[day];
                var next = random.Next(dailyOptions[day].Count - 1);
                dayOptionIndexes[day] = next >= current ? next + 1 : next;
                mutated = true;
            }

            if (!mutated && random.NextDouble() < (double)parameters.MutationRate)
            {
                var day = random.Next(DaysInWeek);

                if (dailyOptions[day].Count > 1)
                {
                    var current = dayOptionIndexes[day];
                    var next = random.Next(dailyOptions[day].Count - 1);
                    dayOptionIndexes[day] = next >= current ? next + 1 : next;
                }
            }
        }

        private static bool Overlaps(CandidateShift first, CandidateShift second) =>
            first.StartAbsoluteMinutes < second.EndAbsoluteMinutes &&
            second.StartAbsoluteMinutes < first.EndAbsoluteMinutes;

        private static Task ReportAsync(
            Func<string, int, string, Task>? reportProgress,
            string stage,
            int progress,
            string message) =>
            reportProgress is null
                ? Task.CompletedTask
                : reportProgress(stage, progress, message);

        private sealed record Individual(int[] DayOptionIndexes, Fitness Fitness);

        private sealed record Fitness(
            long Score,
            int OverlapCount,
            int BreakViolationMinutes,
            int MinimumHoursMissing,
            int TotalShiftCount,
            bool IsComplete);
    }
}
