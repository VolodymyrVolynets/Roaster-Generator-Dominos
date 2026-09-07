using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Roaster_Generator.Configuration;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;

namespace Roaster_Generator.Services;

public sealed class RosterGenerationException(string message) : Exception(message);

public sealed class RosterGenerationAlgorithm(
    AppDbContext db,
    IOptions<ShopHoursOptions> shopHoursOptions)
{
    private const int MinimumShiftHours = 3;
    private const int PreferredMinimumShiftHours = 6;
    private const int MaximumShiftHours = 10;
    private const int MinimumBreakMinutes = 7 * 60;
    private const int HoursInWeek = 7 * 24;
    private const int PopulationSize = 24;
    private const int GenerationCount = 150;
    private readonly ShopHoursOptions shopHours = shopHoursOptions.Value;

    public async Task<RosterPlan> GenerateAsync(
        DateOnly weekStart,
        CancellationToken cancellationToken)
    {
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
            demand);

        if (candidates.Count == 0)
        {
            throw new RosterGenerationException(
                "No valid 3–10 hour shifts can be created from the employees' availability and shop hours.");
        }

        var solver = new GeneticRosterSolver(candidates, demand, employees);
        var selectedCandidates = solver.Solve();

        if (selectedCandidates is null)
        {
            throw new RosterGenerationException(
                "No exact roster could be generated. Check that every active employee has availability, that non-solo drivers overlap another driver, and that the demand can be covered with 3–10 hour shifts and 7 hours between shifts.");
        }

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
        IReadOnlyList<int> demand)
    {
        var employeeIndexes = employees
            .Select((employee, index) => (employee.Id, Index: index))
            .ToDictionary(item => item.Id, item => item.Index);
        var candidates = new List<CandidateShift>();

        foreach (var availabilityShift in availability)
        {
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
                for (var duration = MinimumShiftHours; duration <= MaximumShiftHours; duration++)
                {
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

                    if (!employees[employeeIndex].CanWorkAlone &&
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

    private sealed class GeneticRosterSolver(
        IReadOnlyList<CandidateShift> candidates,
        IReadOnlyList<int> demand,
        IReadOnlyList<Employee> employees)
    {
        private readonly Random random = new(20260907);
        private readonly List<int>[] candidatesBySlot = BuildCandidatesBySlot(candidates);

        public IReadOnlyList<CandidateShift>? Solve()
        {
            var population = Enumerable.Range(0, PopulationSize)
                .Select(_ => CreateIndividual())
                .ToList();

            for (var generation = 0; generation < GenerationCount; generation++)
            {
                population = population
                    .OrderBy(individual => individual.Score)
                    .ToList();

                if (population[0].IsComplete)
                {
                    return population[0].Selected.Select(index => candidates[index]).ToList();
                }

                var nextPopulation = population.Take(2).ToList();

                while (nextPopulation.Count < PopulationSize)
                {
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

            var exactSelection = TryExactSearch(best.Priorities);
            return exactSelection?.Select(index => candidates[index]).ToList();
        }

        private Individual CreateIndividual()
        {
            var priorities = candidates.Select(_ => random.NextDouble()).ToArray();
            return Evaluate(priorities);
        }

        private Individual Evaluate(double[] priorities)
        {
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
                + targetDeviation * 100L
                + shortShiftPenalty * 10L
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
                var bestIndex = -1;
                var bestScore = double.MinValue;

                for (var index = 0; index < candidates.Count; index++)
                {
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
                    var longShiftBonus = candidate.DurationHours >= PreferredMinimumShiftHours ? 25 : 0;
                    var score = gain * 10_000D
                        + longShiftBonus
                        - targetDifference * 4D
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

        private IReadOnlyList<int>? TryExactSearch(IReadOnlyList<double> priorities)
        {
            var remaining = demand.ToArray();
            var employeeHours = new int[employees.Count];
            var selected = new List<int>();
            var nodes = 0;

            return Search() ? selected.ToList() : null;

            bool Search()
            {
                if (++nodes > 500_000)
                {
                    return false;
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
                    .OrderByDescending(index => candidates[index].DurationHours >= PreferredMinimumShiftHours)
                    .ThenBy(index => Math.Abs(
                        employeeHours[candidates[index].EmployeeIndex] + candidates[index].DurationHours -
                        employees[candidates[index].EmployeeIndex].TargetHours))
                    .ThenByDescending(index => priorities[index])
                    .ToList();

                foreach (var index in options)
                {
                    var candidate = candidates[index];
                    selected.Add(index);
                    employeeHours[candidate.EmployeeIndex] += candidate.DurationHours;

                    foreach (var slot in candidate.CoveredSlots)
                    {
                        remaining[slot]--;
                    }

                    if (Search())
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

        private static List<int>[] BuildCandidatesBySlot(IReadOnlyList<CandidateShift> candidates)
        {
            var result = Enumerable.Range(0, HoursInWeek)
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

        private Individual Tournament(IReadOnlyList<Individual> population)
        {
            var first = population[random.Next(population.Count)];
            var second = population[random.Next(population.Count)];
            return first.Score <= second.Score ? first : second;
        }

        private double[] Crossover(IReadOnlyList<double> first, IReadOnlyList<double> second)
        {
            var child = new double[first.Count];

            for (var index = 0; index < child.Length; index++)
            {
                child[index] = random.NextDouble() < 0.5 ? first[index] : second[index];
            }

            return child;
        }

        private void Mutate(double[] priorities)
        {
            for (var index = 0; index < priorities.Length; index++)
            {
                if (random.NextDouble() < 0.03)
                {
                    priorities[index] = random.NextDouble();
                }
            }
        }

        private static int GetUnpleasantHoursPenalty(CandidateShift candidate)
        {
            var startHour = candidate.StartTime.Hour;
            var finishHour = candidate.FinishTime.Hour;
            var lateFinish = finishHour <= 6
                ? Math.Max(0, finishHour + 24 - 22)
                : Math.Max(0, finishHour - 22);
            var earlyStart = Math.Max(0, 8 - startHour);

            return lateFinish * 2 + earlyStart;
        }

        private sealed record Individual(
            double[] Priorities,
            IReadOnlyList<int> Selected,
            long Score,
            bool IsComplete);
    }
}
