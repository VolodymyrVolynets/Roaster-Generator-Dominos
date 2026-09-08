using System.Diagnostics;
using System.Globalization;
using Google.OrTools.Sat;
using Roaster_Generator.Entities;
using Roaster_Generator.Enums;
using Roaster_Generator.Validation;

namespace Roaster_Generator.Services;

/// <summary>
/// Enumerates every legal 3–10 hour shift starting no later than the configured latest start, then solves exact coverage with CP-SAT.
/// Coverage, availability, supervision and minimum rest are hard constraints; only
/// allocation fairness, shift shape and additional rest are weighted preferences.
/// </summary>
public sealed class RosterSolver
{
    private const int MaxCandidates = 100_000;
    private const int MaxEmployees = RosterSolverInputValidator.MaxEmployees;
    private const int PercentageScale = 10_000;

    public RosterSolverResult Solve(
        RosterSolverInput input,
        Action<RosterSolverProgress>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        var clock = Stopwatch.StartNew();
        var reporter = new ProgressReporter(onProgress);
        cancellationToken.ThrowIfCancellationRequested();
        reporter.Publish("validating", 5, $"Checking demand, availability, employee targets, rest settings and the hard {input.Options.LatestShiftStartHour:00}:00 latest shift start. Overnight finishes are allowed.");
        var inputErrors = RosterSolverInputValidator.ValidateInput(input);
        if (inputErrors.Count > 0)
            return Failure("invalid", "Roster inputs are invalid; no roster was generated.", inputErrors);

        var employees = input.Employees.Where(employee => employee.IsActive).OrderBy(employee => employee.Id).ToArray();
        var totalDemand = input.Demand.Sum(slot => slot.RequiredDrivers);
        var totalTargets = employees.Where(employee => TargetHours(employee) > 0).Sum(TargetHours);
        var utilization = totalTargets == 0 ? 0 : 100.0 * totalDemand / totalTargets;
        if (employees.Length > MaxEmployees)
            return Failure("capacity-exceeded", $"This server supports at most {MaxEmployees} active employees per solve.");
        if (totalDemand == 0)
            return Result("optimal", "Demand is zero for every hour; the correct roster is empty.", [], [], 0, 0);

        var demand = input.Demand.ToDictionary(slot => (slot.Date, slot.Hour));
        var boundary = input.BoundaryShifts.GroupBy(shift => shift.EmployeeId)
            .ToDictionary(group => group.Key, group => group.OrderBy(shift => shift.Start).ToArray());
        var employeeById = employees.ToDictionary(employee => employee.Id);
        var candidates = new List<Candidate>();
        foreach (var availability in input.Availability.OrderBy(shift => shift.EmployeeId).ThenBy(shift => shift.Date))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!employeeById.TryGetValue(availability.EmployeeId, out var employee) ||
                availability.Date < input.WeekStart || availability.Date >= input.WeekStart.AddDays(7))
                continue;

            var (windowStart, windowFinish) = AvailabilityWindow(availability);
            var existing = boundary.GetValueOrDefault(employee.Id) ?? [];
            for (var start = windowStart; start <= Math.Min(input.Options.LatestShiftStartHour, windowFinish - 3); start++)
            {
                for (var length = 3; length <= 10 && start + length <= windowFinish; length++)
                {
                    var finish = start + length;
                    var usable = true;
                    for (var hour = start; hour < finish; hour++)
                    {
                        // Never create coverage at zero/unentered demand. A non-car driver
                        // cannot be selected for a slot requiring only one driver.
                        if (!demand.TryGetValue((availability.Date, hour), out var slot) ||
                            slot.RequiredDrivers == 0 || (!IsCarDriver(employee) && slot.RequiredDrivers == 1))
                        {
                            usable = false;
                            break;
                        }
                    }
                    if (!usable) continue;

                    var actualStart = availability.Date.ToDateTime(TimeOnly.MinValue).AddHours(start);
                    var actualFinish = availability.Date.ToDateTime(TimeOnly.MinValue).AddHours(finish);
                    if (existing.Any(shift => GapHours(actualStart, actualFinish, shift.Start, shift.Finish) < input.Options.MinimumRestHours))
                        continue;

                    // Only the nearest saved shift on either side contributes extra-rest preference.
                    var previous = existing.LastOrDefault(shift => shift.Finish <= actualStart);
                    var next = existing.FirstOrDefault(shift => shift.Start >= actualFinish);
                    var previousRestPenalty = previous is null ? 0 : Math.Max(0, input.Options.PreferredRestHours - (actualStart - previous.Finish).TotalHours);
                    var nextRestPenalty = next is null ? 0 : Math.Max(0, input.Options.PreferredRestHours - (next.Start - actualFinish).TotalHours);
                    candidates.Add(new Candidate(candidates.Count, employee.Id, IsCarDriver(employee),
                        availability.Date, start, finish, AbsoluteHour(input.WeekStart, availability.Date, start),
                        (long)Math.Ceiling(previousRestPenalty * 100), (long)Math.Ceiling(nextRestPenalty * 100)));
                    if (candidates.Count > MaxCandidates)
                        return Failure("capacity-exceeded", $"More than {MaxCandidates:N0} legal shifts would be needed. Reduce the employee pool or availability windows; no candidates were silently discarded.");
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        reporter.Publish("building-model", 15, $"Prepared {candidates.Count:N0} legal shifts for {employees.Length} employees. Exact demand: {totalDemand} driver-hours; shared target utilization: {utilization:F1}%.");
        var coverage = input.Demand.Where(slot => slot.RequiredDrivers > 0).ToDictionary(
            slot => AbsoluteHour(input.WeekStart, slot.Date, slot.Hour), _ => new List<Candidate>());
        foreach (var candidate in candidates)
            for (var hour = candidate.AbsoluteStart; hour < candidate.AbsoluteFinish; hour++)
                coverage[hour].Add(candidate);

        var shortages = new List<string>();
        foreach (var slot in input.Demand.Where(slot => slot.RequiredDrivers > 0).OrderBy(slot => slot.Date).ThenBy(slot => slot.Hour))
        {
            var choices = coverage[AbsoluteHour(input.WeekStart, slot.Date, slot.Hour)];
            var available = choices.Select(candidate => candidate.EmployeeId).Distinct().Count();
            if (available < slot.RequiredDrivers)
                shortages.Add($"{FormatSlot(slot)}: need {slot.RequiredDrivers - available} more driver(s). Demand {slot.RequiredDrivers}; only {available} can cover this hour in a legal 3–10 hour shift within availability, the {input.Options.LatestShiftStartHour:00}:00 latest shift start and the {input.Options.MinimumRestHours}-hour rest rule. Shifts may finish overnight, but cannot start after {input.Options.LatestShiftStartHour:00}:00 or after midnight.");
            if (!choices.Any(candidate => candidate.IsCar))
                shortages.Add($"{FormatSlot(slot)}: need at least one car driver to cover this hour alone.");
        }
        if (shortages.Count > 0)
            return Failure("infeasible", "Exact coverage is impossible with the current availability, supervision and shift limits.", shortages, candidates.Count);

        var diagnosticReserve = Math.Min(3.0, input.Options.MaxSolveSeconds * 0.2);
        // Find a complete schedule before introducing fairness tables and other preferences.
        // On small servers, optimization presolve must never consume the whole budget before
        // retaining a valid roster that a much smaller feasibility model can find quickly.
        var model = BuildModel(input, employees, candidates, coverage, false, cancellationToken, optimizePreferences: false);
        var modelError = model.Model.Validate();
        if (!string.IsNullOrEmpty(modelError))
            return Failure("invalid", "The scheduling model could not be validated.", [modelError], candidates.Count);

        reporter.Publish("solving", 25, $"Solving exact hourly coverage with one CPU worker and a {input.Options.MaxSolveSeconds}-second total budget. Coverage, minimum rest and the {input.Options.LatestShiftStartHour:00}:00 latest shift start cannot be traded for a better score.");
        var remaining = input.Options.MaxSolveSeconds - clock.Elapsed.TotalSeconds - diagnosticReserve;
        if (remaining <= 0)
            return Failure("timed-out", "The time budget expired while preparing the model. Feasibility has not been determined; increase the solve limit.", candidateCount: candidates.Count);

        using var solver = NewSolver(remaining);
        var status = RunSearch(solver, model.Model, reporter, false, remaining, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (status is CpSolverStatus.Optimal or CpSolverStatus.Feasible)
        {
            var shifts = Extract(solver, candidates, model.Selected);
            var errors = Validate(input, shifts);
            if (errors.Count > 0)
                return Failure("invalid", "Generated shifts failed independent validation; nothing may be saved.", errors, candidates.Count);
            if (new[] { input.Options.TargetHoursWeight, input.Options.HistoryFairnessWeight, input.Options.FairnessSpreadWeight, input.Options.LongShiftBonus, input.Options.ShortShiftPenalty,
                input.Options.DailyShiftCountPenalty, input.Options.ShortBreakPenalty }.All(weight => weight == 0))
                return Result("optimal", "Exact demand is covered. All preference weights are disabled.", shifts, [], candidates.Count, 0);

            reporter.Publish("optimizing", 45, "A complete roster is independently validated. Improving proportional fairness, shift lengths and consecutive-shift rest while retaining this feasible result.");
            var selectedCandidates = candidates.Where(candidate => solver.BooleanValue(model.Selected[candidate.Index])).ToList();
            var heuristicBudget = Math.Min(0.65, Math.Max(0, input.Options.MaxSolveSeconds - clock.Elapsed.TotalSeconds) * 0.35);
            selectedCandidates = ImproveIncumbent(input, employees, candidates, selectedCandidates, heuristicBudget, cancellationToken);
            shifts = selectedCandidates.Select(candidate => new RosterSolverShift(candidate.EmployeeId, candidate.Date, candidate.Start, candidate.Finish)).ToArray();
            errors = Validate(input, shifts);
            if (errors.Count > 0)
                return Failure("invalid", "The balanced initial roster failed independent validation; nothing may be saved.", errors, candidates.Count);
            var hintIndexes = selectedCandidates.Select(candidate => candidate.Index).ToHashSet();
            remaining = input.Options.MaxSolveSeconds - clock.Elapsed.TotalSeconds;
            if (remaining > 0.02)
            {
                var preferenceModel = BuildModel(input, employees, candidates, coverage, false, cancellationToken);
                foreach (var candidate in candidates)
                    preferenceModel.Model.AddHint(preferenceModel.Selected[candidate.Index], hintIndexes.Contains(candidate.Index) ? 1 : 0);
                remaining = input.Options.MaxSolveSeconds - clock.Elapsed.TotalSeconds;
                if (remaining > 0.01)
                {
                    using var preferenceSolver = NewSolver(remaining);
                    var preferenceStatus = RunSearch(preferenceSolver, preferenceModel.Model, reporter, false, remaining, cancellationToken, hasSolution: true);
                    if (preferenceStatus is CpSolverStatus.Optimal or CpSolverStatus.Feasible)
                    {
                        var improved = Extract(preferenceSolver, candidates, preferenceModel.Selected);
                        errors = Validate(input, improved);
                        if (errors.Count > 0)
                            return Failure("invalid", "Optimized shifts failed independent validation; nothing may be saved.", errors, candidates.Count);
                        var improvedCandidates = candidates.Where(candidate => preferenceSolver.BooleanValue(preferenceModel.Selected[candidate.Index])).ToArray();
                        var score = CreateScorer(input, employees);
                        // A partial native hint is not itself a retained CP-SAT incumbent.
                        // Never replace our known, validated roster with a worse search result.
                        // Compare unrounded business costs, including historical correction and rest.
                        if (score(improvedCandidates) > score(selectedCandidates) + 0.01)
                            return Result("feasible", "Exact demand is covered by the validated, locally balanced roster. It has a better weighted score than the global search result and was retained; global optimality has not been established.", shifts, [], candidates.Count, null);
                        return Result(preferenceStatus == CpSolverStatus.Optimal ? "optimal" : "feasible",
                            preferenceStatus == CpSolverStatus.Optimal
                                ? "Exact demand is covered and the best weighted schedule was proven within the configured rules."
                                : "Exact demand is covered. The time limit was reached before the best possible preference score was proven.",
                            improved, [], candidates.Count, preferenceSolver.ObjectiveValue);
                    }
                    if (preferenceStatus == CpSolverStatus.ModelInvalid)
                        return Failure("invalid", "The solver rejected the preference model.", [preferenceSolver.Response?.SolutionInfo ?? "No model details were returned."], candidates.Count);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            return Result("feasible", "Exact demand is covered by a validated, locally balanced roster. The global preference search reached its limit; increase the solve time to improve or prove the best fairness, shifts and rest score.", shifts, [], candidates.Count, null);
        }
        if (status == CpSolverStatus.ModelInvalid)
            return Failure("invalid", "The solver rejected the scheduling model.", [solver.Response?.SolutionInfo ?? "No model details were returned."], candidates.Count);

        var provenImpossible = status == CpSolverStatus.Infeasible;
        reporter.Publish("diagnosing", 90, provenImpossible
            ? "Exact coverage is impossible under the combined rules. Locating the hours that remain uncovered in the closest legal assignment."
            : "No complete roster was found before the search limit. Feasibility is unproven; checking a diagnostic assignment for specific gaps.");
        var diagnostics = new List<string>();
        remaining = input.Options.MaxSolveSeconds - clock.Elapsed.TotalSeconds;
        if (remaining > 0.05)
        {
            var diagnosticModel = BuildModel(input, employees, candidates, coverage, true, cancellationToken);
            remaining = input.Options.MaxSolveSeconds - clock.Elapsed.TotalSeconds;
            if (remaining > 0.01)
            {
                using var diagnosticSolver = NewSolver(remaining);
                var diagnosticStatus = RunSearch(diagnosticSolver, diagnosticModel.Model, reporter, true, remaining, cancellationToken);
                if (diagnosticStatus is CpSolverStatus.Feasible or CpSolverStatus.Optimal)
                {
                    var diagnosticShifts = Extract(diagnosticSolver, candidates, diagnosticModel.Selected);
                    var missingTotal = diagnosticModel.Missing.Sum(item => (int)diagnosticSolver.Value(item.Missing));
                    if (missingTotal == 0 && !provenImpossible && Validate(input, diagnosticShifts).Count == 0)
                        return Result("feasible", "Exact coverage was found during the diagnostic search. Preferences have not been optimized; increase the solve limit to improve fairness and shift quality.", diagnosticShifts, [], candidates.Count, null);

                    diagnostics.Add(diagnosticStatus == CpSolverStatus.Optimal
                        ? $"At least {missingTotal} driver-hour(s) must remain uncovered under the current rules. The following is one minimum-shortage allocation; its shifts will not be saved."
                        : $"The best diagnostic assignment found leaves {missingTotal} driver-hour(s) uncovered. This is an example of conflicting hours, not a proof that these exact gaps are unavoidable; its shifts will not be saved.");
                    foreach (var item in diagnosticModel.Missing)
                    {
                        var missing = (int)diagnosticSolver.Value(item.Missing);
                        if (missing > 0)
                            diagnostics.Add($"{FormatSlot(item.Slot)}: need {missing} more driver(s) in the diagnostic assignment (demand {item.Slot.RequiredDrivers}). Availability, the {input.Options.LatestShiftStartHour:00}:00 latest shift start, one shift per day, supervision or the {input.Options.MinimumRestHours}-hour minimum rest prevents filling every hour together.");
                    }
                }
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (diagnostics.Count == 0)
            diagnostics.Add($"No diagnostic assignment was found within the remaining budget. Check availability, 3–10 hour shift continuity, the {input.Options.LatestShiftStartHour:00}:00 latest shift start, supervision and neighboring-week rest, or increase the solve limit.");
        return Failure(provenImpossible ? "infeasible" : "timed-out", provenImpossible
            ? "Exact coverage is impossible under the current hard constraints. No roster was saved."
            : "The search time limit was reached without a complete roster. This does not prove the demand is impossible; increase the solve limit or adjust availability. No roster was saved.", diagnostics, candidates.Count);

        RosterSolverResult Failure(string state, string message, IReadOnlyList<string>? errors = null, int candidateCount = 0) => new()
        {
            Status = state, Message = message, Diagnostics = errors ?? [message],
            WallTimeSeconds = clock.Elapsed.TotalSeconds, CandidateCount = candidateCount,
            TotalDemandHours = input.Demand.Where(slot => slot.RequiredDrivers >= 0).Sum(slot => (long)slot.RequiredDrivers) is var sum && sum <= int.MaxValue ? (int)sum : 0
        };

        RosterSolverResult Result(string state, string message, IReadOnlyList<RosterSolverShift> shifts,
            IReadOnlyList<string> errors, int count, double? objective) => new()
        {
            Status = state, Message = message, Shifts = shifts, Diagnostics = errors.Concat(FairnessWarnings(input, shifts)).ToArray(),
            TotalDemandHours = totalDemand, TotalScheduledHours = shifts.Sum(shift => shift.DurationHours),
            WallTimeSeconds = clock.Elapsed.TotalSeconds, CandidateCount = count,
            ObjectiveValue = objective, TargetUtilizationPercent = utilization
        };
    }

    /// <summary>Checks actual shifts independently of the optimization model before persistence.</summary>
    public static IReadOnlyList<string> Validate(RosterSolverInput input, IReadOnlyList<RosterSolverShift> shifts)
    {
        var errors = RosterSolverInputValidator.ValidateInput(input).ToList();
        if (errors.Count > 0) return errors;
        var employees = input.Employees.Where(employee => employee.IsActive).ToDictionary(employee => employee.Id);
        var availability = input.Availability.GroupBy(shift => (shift.EmployeeId, shift.Date))
            .ToDictionary(group => group.Key, group => group.ToArray());
        var expected = input.Demand.ToDictionary(slot => AbsoluteHour(input.WeekStart, slot.Date, slot.Hour));
        var actual = new Dictionary<int, List<Employee>>();
        var times = new Dictionary<Guid, List<(DateTime Start, DateTime Finish, bool Generated)>>();
        foreach (var group in shifts.GroupBy(shift => (shift.EmployeeId, shift.Date)))
            if (group.Count() > 1) errors.Add($"{group.Key.Date:dddd yyyy-MM-dd}: employee {group.Key.EmployeeId} has more than one shift.");

        foreach (var shift in shifts)
        {
            if (!employees.TryGetValue(shift.EmployeeId, out var employee))
            {
                errors.Add($"Shift references inactive or unknown employee {shift.EmployeeId}.");
                continue;
            }
            if (shift.Date < input.WeekStart || shift.Date >= input.WeekStart.AddDays(7) ||
                shift.StartHour is < 0 or > 47 || shift.FinishHour > 48 || shift.DurationHours is < 3 or > 10)
            {
                errors.Add($"{shift.Date:dddd}: {Name(employee)} has an invalid date or shift length; shifts must last 3–10 hours.");
                continue;
            }
            if (shift.StartHour > input.Options.LatestShiftStartHour)
                errors.Add($"{shift.Date:dddd}: {Name(employee)} starts at {shift.StartHour % 24:00}:00{(shift.StartHour >= 24 ? " (+1 day)" : string.Empty)}. Every generated shift must start by {input.Options.LatestShiftStartHour:00}:00; after-midnight starts are also forbidden. Overnight finishes are allowed.");
            if (!availability.TryGetValue((employee.Id, shift.Date), out var windows) ||
                !windows.Any(window => { var (start, finish) = AvailabilityWindow(window); return start <= shift.StartHour && finish >= shift.FinishHour; }))
                errors.Add($"{shift.Date:dddd}: {Name(employee)} is scheduled outside their availability.");

            for (var hour = shift.StartHour; hour < shift.FinishHour; hour++)
            {
                var key = AbsoluteHour(input.WeekStart, shift.Date, hour);
                if (!actual.TryGetValue(key, out var people)) actual[key] = people = [];
                people.Add(employee);
            }
            if (!times.TryGetValue(employee.Id, out var entries)) times[employee.Id] = entries = [];
            entries.Add((shift.Date.ToDateTime(TimeOnly.MinValue).AddHours(shift.StartHour),
                shift.Date.ToDateTime(TimeOnly.MinValue).AddHours(shift.FinishHour), true));
        }
        foreach (var hour in expected.Keys.Union(actual.Keys).Order())
        {
            var slot = expected.GetValueOrDefault(hour);
            var required = slot?.RequiredDrivers ?? 0;
            var assigned = actual.GetValueOrDefault(hour) ?? [];
            var label = slot is not null ? FormatSlot(slot) : input.WeekStart.ToDateTime(TimeOnly.MinValue).AddHours(hour).ToString("dddd HH:mm", CultureInfo.InvariantCulture);
            if (assigned.Count != required)
                errors.Add($"Demand mismatch: {label}: demand {required}, scheduled {assigned.Count}; {(assigned.Count < required ? $"need {required - assigned.Count} more driver(s)" : $"{assigned.Count - required} excess driver(s)")}.");
            if (required > 0 && !assigned.Any(IsCarDriver))
                errors.Add($"{label}: no car driver is scheduled to cover this hour alone.");
        }
        foreach (var boundary in input.BoundaryShifts)
            if (times.TryGetValue(boundary.EmployeeId, out var entries))
                entries.Add((boundary.Start, boundary.Finish, false));
        foreach (var (id, entries) in times)
        {
            var ordered = entries.OrderBy(shift => shift.Start).ToArray();
            // Compare all pairs involving a new shift, so an overlapping saved interval cannot hide a conflict.
            for (var i = 0; i < ordered.Length; i++)
                for (var j = i + 1; j < ordered.Length; j++)
                    if ((ordered[i].Generated || ordered[j].Generated) &&
                        GapHours(ordered[i].Start, ordered[i].Finish, ordered[j].Start, ordered[j].Finish) < input.Options.MinimumRestHours)
                        errors.Add($"{Name(employees[id])}: shifts ending {ordered[i].Finish:dddd HH:mm} and starting {ordered[j].Start:dddd HH:mm} overlap or have less than {input.Options.MinimumRestHours} hours of rest.");
        }
        return errors;
    }

    private static ModelState BuildModel(RosterSolverInput input, Employee[] employees, List<Candidate> candidates,
        Dictionary<int, List<Candidate>> coverage, bool diagnostic, CancellationToken token, bool optimizePreferences = true)
    {
        var model = new CpModel();
        var selected = candidates.Select(candidate => model.NewBoolVar($"s{candidate.Index}")).ToArray();
        var objective = new List<LinearExpr>();
        var includePreferences = !diagnostic && optimizePreferences;
        var groups = new List<DayVariables>();
        foreach (var group in candidates.GroupBy(candidate => (candidate.EmployeeId, candidate.Date)))
        {
            token.ThrowIfCancellationRequested();
            var options = group.ToArray();
            var variables = options.Select(candidate => selected[candidate.Index]).ToArray();
            var present = model.NewBoolVar($"working{groups.Count}");
            model.Add(LinearExpr.Sum(variables) == present);
            var start = model.NewIntVar(0, options.Max(candidate => candidate.AbsoluteStart), $"start{groups.Count}");
            var finish = model.NewIntVar(0, options.Max(candidate => candidate.AbsoluteFinish), $"finish{groups.Count}");
            model.Add(start == LinearExpr.WeightedSum(variables, options.Select(candidate => (long)candidate.AbsoluteStart)));
            model.Add(finish == LinearExpr.WeightedSum(variables, options.Select(candidate => (long)candidate.AbsoluteFinish)));
            groups.Add(new DayVariables(group.Key.EmployeeId, group.Key.Date, present, start, finish,
                options.Min(candidate => candidate.AbsoluteStart), options.Max(candidate => candidate.AbsoluteFinish), options));
        }
        foreach (var employeeGroups in groups.GroupBy(group => group.EmployeeId))
        {
            token.ThrowIfCancellationRequested();
            var days = employeeGroups.OrderBy(group => group.Date).ToArray();
            for (var i = 0; i < days.Length; i++)
            {
                // Only the first/last worked day is adjacent to an already saved neighboring week.
                // At long preferred-rest settings, charging every day would count working time as rest.
                if (includePreferences && input.Options.ShortBreakPenalty > 0)
                {
                    var day = days[i];
                    var previousCosts = day.Options.Select(candidate => candidate.PreviousBoundaryRestPenalty).ToArray();
                    var nextCosts = day.Options.Select(candidate => candidate.NextBoundaryRestPenalty).ToArray();
                    if (previousCosts.Max() > 0)
                    {
                        var previousPenalty = model.NewIntVar(0, previousCosts.Max(), $"previousRest{objective.Count}");
                        model.Add(previousPenalty >= LinearExpr.WeightedSum(day.Options.Select(candidate => selected[candidate.Index]), previousCosts))
                            .OnlyEnforceIf(days.Take(i).Select(group => group.Present.Not()).ToArray());
                        objective.Add(previousPenalty * input.Options.ShortBreakPenalty);
                    }
                    if (nextCosts.Max() > 0)
                    {
                        var nextPenalty = model.NewIntVar(0, nextCosts.Max(), $"nextRest{objective.Count}");
                        model.Add(nextPenalty >= LinearExpr.WeightedSum(day.Options.Select(candidate => selected[candidate.Index]), nextCosts))
                            .OnlyEnforceIf(days.Skip(i + 1).Select(group => group.Present.Not()).ToArray());
                        objective.Add(nextPenalty * input.Options.ShortBreakPenalty);
                    }
                }
                for (var j = i + 1; j < days.Length; j++)
                {
                    var earlier = days[i];
                    var later = days[j];
                    if (later.EarliestStart - earlier.LatestFinish < input.Options.MinimumRestHours)
                        model.Add(later.Start - earlier.Finish >= input.Options.MinimumRestHours).OnlyEnforceIf([earlier.Present, later.Present]);
                    if (includePreferences && input.Options.ShortBreakPenalty > 0 &&
                        later.EarliestStart - earlier.LatestFinish < input.Options.PreferredRestHours)
                    {
                        var missingRest = model.NewIntVar(0, input.Options.PreferredRestHours, $"rest{objective.Count}");
                        var consecutive = new ILiteral[] { earlier.Present, later.Present }
                            .Concat(days.Skip(i + 1).Take(j - i - 1).Select(group => group.Present.Not())).ToArray();
                        model.Add(missingRest >= input.Options.PreferredRestHours - later.Start + earlier.Finish).OnlyEnforceIf(consecutive);
                        objective.Add(missingRest * (input.Options.ShortBreakPenalty * 100L));
                    }
                }
            }
        }

        var missing = new List<(RosterSolverDemand Slot, IntVar Missing)>();
        foreach (var slot in input.Demand.Where(slot => slot.RequiredDrivers > 0).OrderBy(slot => slot.Date).ThenBy(slot => slot.Hour))
        {
            token.ThrowIfCancellationRequested();
            var options = coverage[AbsoluteHour(input.WeekStart, slot.Date, slot.Hour)];
            var assigned = LinearExpr.Sum(options.Select(candidate => selected[candidate.Index]));
            var qualified = LinearExpr.Sum(options.Where(candidate => candidate.IsCar).Select(candidate => selected[candidate.Index]));
            if (diagnostic)
            {
                var deficit = model.NewIntVar(0, slot.RequiredDrivers, $"missing{missing.Count}");
                model.Add(assigned + deficit == slot.RequiredDrivers);
                // A partially covered hour still cannot be covered only by non-car drivers.
                model.Add(assigned <= qualified * slot.RequiredDrivers);
                missing.Add((slot, deficit));
                objective.Add(deficit);
            }
            else
            {
                model.Add(assigned == slot.RequiredDrivers);
                model.Add(qualified >= 1);
            }
        }

        if (includePreferences)
        {
            var employeesById = employees.ToDictionary(employee => employee.Id);
            var fairness = FairnessContext.Create(input, employees);
            var byEmployee = candidates.GroupBy(candidate => candidate.EmployeeId).ToDictionary(group => group.Key, group => group.ToArray());
            // Convex costs also balance the remaining employees when somebody cannot work.
            // Absolute deviation alone has flat regions that make 5/15 hours tie with 10/10.
            // A 71-value table avoids nonlinear integer multiplication during native search.
        var fairnessTables = employees.Where(employee => TargetHours(employee) > 0).ToDictionary(employee => employee.Id, employee =>
                Enumerable.Range(0, 71).Select(hours => fairness.EmployeeCost(employee, hours)).ToArray());
            foreach (var costs in fairnessTables.Values)
            {
                var minimum = costs.Min();
                for (var index = 0; index < costs.Length; index++) costs[index] -= minimum;
            }
            // A common scale preserves relative employee costs and prevents integer overflow
            // even with extreme demand, tiny targets and the largest allowed admin weights.
            var fairnessScale = Math.Max(1, fairnessTables.Values.SelectMany(values => values).DefaultIfEmpty(0).Max() / 1_000_000_000_000);
            var percentages = new List<IntVar>();
            foreach (var employee in employees)
            {
                token.ThrowIfCancellationRequested();
                if (TargetHours(employee) <= 0) continue;
                var choices = byEmployee.GetValueOrDefault(employee.Id) ?? [];
                var hours = model.NewIntVar(0, 70, $"hours{employee.Id}");
                model.Add(hours == LinearExpr.WeightedSum(choices.Select(candidate => selected[candidate.Index]), choices.Select(candidate => (long)candidate.Length)));
                var costs = fairnessTables[employee.Id].Select(value => (long)Math.Round(value / fairnessScale)).ToArray();
                var deviationCost = model.NewIntVar(0, costs.Max(), $"fairness{employee.Id}");
                model.AddElement(hours, costs, deviationCost);
                objective.Add(deviationCost);
                if (input.Options.FairnessSpreadWeight > 0)
                {
                    var percentage = model.NewIntVar(0, 700_000, $"utilization{employee.Id}");
                    model.AddDivisionEquality(percentage, hours * PercentageScale, TargetHours(employee));
                    percentages.Add(percentage);
                }
            }
            if (percentages.Count > 1 && input.Options.FairnessSpreadWeight > 0)
            {
                var maximum = model.NewIntVar(0, 700_000, "maximumUtilization");
                var minimum = model.NewIntVar(0, 700_000, "minimumUtilization");
                model.AddMaxEquality(maximum, percentages);
                model.AddMinEquality(minimum, percentages);
                var excess = model.NewIntVar(0, 700_000, "excessUtilizationSpread");
                model.AddMaxEquality(excess, new LinearExpr[] { LinearExpr.Constant(0), maximum - minimum - 3000 });
                var squared = model.NewIntVar(0, 490_000_000_000, "squaredExcessSpread");
                model.AddMultiplicationEquality(squared, excess, excess);
                var scaled = model.NewIntVar(0, 4_900_000_000, "scaledExcessSpread");
                model.AddDivisionEquality(scaled, squared, 100);
                objective.Add(scaled * input.Options.FairnessSpreadWeight);
            }
            foreach (var candidate in candidates)
            {
                var shapePenalty = candidate.Length <= 8
                    ? (8 - candidate.Length) * (long)input.Options.LongShiftBonus + Math.Max(0, 6 - candidate.Length) * (long)input.Options.ShortShiftPenalty
                    : (candidate.Length - 6) * (long)input.Options.LongShiftBonus + (candidate.Length - 8) * (long)input.Options.ShortShiftPenalty;
                var cost = (shapePenalty + input.Options.DailyShiftCountPenalty) * 100;
                // A zero target has no defined utilization percentage. These employees remain
                // available as reserves, with a cost rather than being silently excluded from coverage.
                if (TargetHours(employeesById[candidate.EmployeeId]) == 0)
                    cost += candidate.Length * (long)input.Options.TargetHoursWeight * PercentageScale;
                if (cost > 0) objective.Add(selected[candidate.Index] * cost);
            }
        }
        model.Minimize(LinearExpr.Sum(objective));
        return new ModelState(model, selected, missing);
    }

    private static CpSolver NewSolver(double seconds) => new()
    {
        StringParameters = $"max_time_in_seconds:{Math.Max(0.001, seconds).ToString("F4", CultureInfo.InvariantCulture)} num_search_workers:1 random_seed:0 log_search_progress:false"
    };

    private static CpSolverStatus RunSearch(CpSolver solver, CpModel model, ProgressReporter reporter,
        bool diagnostic, double seconds, CancellationToken token, bool hasSolution = false)
    {
        token.ThrowIfCancellationRequested();
        using var callback = new SearchCallback(reporter, diagnostic, token) { HasSolution = hasSolution };
        var started = Stopwatch.StartNew();
        using var registration = token.Register(solver.StopSearch);
        // Also poll cancellation, closing the tiny gap between registration and the native solver starting.
        using var heartbeat = new Timer(_ =>
        {
            if (token.IsCancellationRequested) { solver.StopSearch(); return; }
            reporter.Pulse(diagnostic ? "diagnosing" : callback.HasSolution ? "optimizing" : "solving",
                diagnostic ? 92 : Math.Min(85, 25 + (int)(60 * started.Elapsed.TotalSeconds / Math.Max(1, seconds))),
                diagnostic ? $"Locating conflicting demand hours ({started.Elapsed.TotalSeconds:F0}s)."
                    : callback.HasSolution ? $"Exact coverage found; improving fairness, shifts and rest ({started.Elapsed.TotalSeconds:F0}s)."
                    : $"Searching for complete coverage ({started.Elapsed.TotalSeconds:F0}s); availability and rest constraints remain enforced.");
        }, null, TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250));
        var status = solver.Solve(model, callback);
        token.ThrowIfCancellationRequested();
        return status;
    }

    private static IReadOnlyList<RosterSolverShift> Extract(CpSolver solver, List<Candidate> candidates, BoolVar[] selected) =>
        candidates.Where(candidate => solver.BooleanValue(selected[candidate.Index]))
            .Select(candidate => new RosterSolverShift(candidate.EmployeeId, candidate.Date, candidate.Start, candidate.Finish))
            .OrderBy(shift => shift.Date).ThenBy(shift => shift.StartHour).ThenBy(shift => shift.EmployeeId).ToArray();

    /// <summary>Bounded local improvement of an already exact roster; no coverage is added or removed.</summary>
    private static List<Candidate> ImproveIncumbent(RosterSolverInput input, Employee[] employees,
        List<Candidate> candidates, List<Candidate> current, double seconds, CancellationToken token)
    {
        if (seconds <= 0.01) return current;
        var watch = Stopwatch.StartNew();
        var fairness = FairnessContext.Create(input, employees);
        var employeeById = employees.ToDictionary(employee => employee.Id);
        var choices = candidates.GroupBy(candidate => (candidate.Date, candidate.Start, candidate.Finish))
            .ToDictionary(group => group.Key, group => group.ToArray());
        var byEmployeeShape = candidates.ToDictionary(candidate => (candidate.EmployeeId, candidate.Date, candidate.Start, candidate.Finish));
        var Score = CreateScorer(input, employees);
        var currentScore = Score(current);

        // First distribute entire shifts. This removes solver symmetry (e.g. two drivers
        // receiving a whole week while equally available colleagues get zero) very cheaply.
        Reassign();
        for (var iteration = 0; iteration < 40 && watch.Elapsed.TotalSeconds < seconds; iteration++)
        {
            token.ThrowIfCancellationRequested();
            List<Candidate>? best = null;
            var bestScore = currentScore;
            var hours = current.GroupBy(candidate => candidate.EmployeeId).ToDictionary(group => group.Key, group => group.Sum(candidate => candidate.Length));
            foreach (var shift in current.ToArray())
            {
                if (watch.Elapsed.TotalSeconds >= seconds) break;
                // A split remains inside the original coverage interval. All combinations
                // considered here are existing, fully legal candidates from the full model.
                for (var split = shift.Start + 3; split <= shift.Finish - 3; split++)
                {
                    if (!choices.TryGetValue((shift.Date, shift.Start, split), out var left) ||
                        !choices.TryGetValue((shift.Date, split, shift.Finish), out var right)) continue;
                    var leftChoices = left.OrderBy(candidate => MarginalCost(candidate, hours)).Take(4)
                        .Concat(left.Where(candidate => candidate.EmployeeId == shift.EmployeeId)).Distinct().ToArray();
                    var rightChoices = right.OrderBy(candidate => MarginalCost(candidate, hours)).Take(4)
                        .Concat(right.Where(candidate => candidate.EmployeeId == shift.EmployeeId)).Distinct().ToArray();
                    foreach (var first in leftChoices)
                        foreach (var second in rightChoices)
                            Consider([shift], [first, second]);
                }
            }
            // Slide the boundary between adjacent shifts to balance 3/7 into 5/5, for example.
            var ordered = current.OrderBy(candidate => candidate.AbsoluteStart).ToArray();
            foreach (var first in ordered)
            {
                if (watch.Elapsed.TotalSeconds >= seconds) break;
                foreach (var second in ordered.Where(candidate => candidate.Date == first.Date && candidate.Start == first.Finish))
                {
                    for (var split = Math.Max(first.Start + 3, second.Finish - 10); split <= Math.Min(first.Start + 10, second.Finish - 3); split++)
                        if (byEmployeeShape.TryGetValue((first.EmployeeId, first.Date, first.Start, split), out var left) &&
                            byEmployeeShape.TryGetValue((second.EmployeeId, second.Date, split, second.Finish), out var right))
                            Consider([first, second], [left, right]);
                    if (choices.TryGetValue((first.Date, first.Start, second.Finish), out var merged))
                        foreach (var replacement in merged) Consider([first, second], [replacement]);
                }
            }
            if (best is null) break;
            current = best;
            currentScore = bestScore;
            Reassign();

            void Consider(Candidate[] removed, Candidate[] added)
            {
                if (watch.Elapsed.TotalSeconds >= seconds || !TryChange(removed, added, out var proposal)) return;
                var score = Score(proposal);
                if (score < bestScore - 0.01) { bestScore = score; best = proposal; }
            }
        }
        return current;

        void Reassign()
        {
            for (var pass = 0; pass < 4 && watch.Elapsed.TotalSeconds < seconds; pass++)
            {
                var changed = false;
                foreach (var shift in current.OrderByDescending(candidate => candidate.Length).ToArray())
                {
                    token.ThrowIfCancellationRequested();
                    if (watch.Elapsed.TotalSeconds >= seconds) return;
                    if (!current.Contains(shift)) continue;
                    List<Candidate>? best = null;
                    var bestScore = currentScore;
                    foreach (var replacement in choices[(shift.Date, shift.Start, shift.Finish)])
                    {
                        if (watch.Elapsed.TotalSeconds >= seconds) break;
                        if (replacement.EmployeeId == shift.EmployeeId || !TryChange([shift], [replacement], out var proposal)) continue;
                        var score = Score(proposal);
                        if (score < bestScore - 0.01) { best = proposal; bestScore = score; }
                    }
                    if (best is not null) { current = best; currentScore = bestScore; changed = true; }
                }
                if (!changed) break;
            }
        }

        double MarginalCost(Candidate candidate, Dictionary<Guid, int> hours)
        {
            var employee = employeeById[candidate.EmployeeId];
            var prior = hours.GetValueOrDefault(employee.Id);
            return fairness.EmployeeCost(employee, prior + candidate.Length) - fairness.EmployeeCost(employee, prior);
        }

        bool TryChange(Candidate[] removed, Candidate[] added, out List<Candidate> proposal)
        {
            proposal = [];
            var remaining = current.Where(candidate => !removed.Contains(candidate)).ToArray();
            for (var i = 0; i < added.Length; i++)
            {
                var addition = added[i];
                foreach (var other in remaining.Concat(added.Take(i)).Where(candidate => candidate.EmployeeId == addition.EmployeeId))
                    if (other.Date == addition.Date ||
                        (addition.AbsoluteStart < other.AbsoluteFinish + input.Options.MinimumRestHours &&
                         other.AbsoluteStart < addition.AbsoluteFinish + input.Options.MinimumRestHours)) return false;
            }
            // Coverage counts stay identical by construction; only car-driver coverage can change.
            foreach (var hour in removed.SelectMany(candidate => Enumerable.Range(candidate.AbsoluteStart, candidate.Length)).Distinct())
                if (!remaining.Concat(added).Any(candidate => candidate.IsCar && candidate.AbsoluteStart <= hour && candidate.AbsoluteFinish > hour))
                    return false;
            proposal = remaining.Concat(added).ToList();
            return true;
        }

    }

    private static Func<IReadOnlyList<Candidate>, double> CreateScorer(RosterSolverInput input, Employee[] employees)
    {
        var fairness = FairnessContext.Create(input, employees);
        var employeeById = employees.ToDictionary(employee => employee.Id);
        var boundaryByEmployee = input.BoundaryShifts.GroupBy(shift => shift.EmployeeId).ToDictionary(group => group.Key, group => group.ToArray());
        return Score;

        double Score(IReadOnlyList<Candidate> selected)
        {
            var grouped = selected.GroupBy(candidate => candidate.EmployeeId).ToDictionary(group => group.Key, group => group.ToArray());
            var percentages = new List<double>();
            double score = 0;
            foreach (var employee in employees)
            {
                var shifts = grouped.GetValueOrDefault(employee.Id) ?? [];
                var hours = shifts.Sum(shift => shift.Length);
                score += fairness.EmployeeCost(employee, hours);
                if (TargetHours(employee) > 0) percentages.Add(hours * 100.0 / TargetHours(employee));
                if (input.Options.ShortBreakPenalty > 0 && shifts.Length > 0)
                {
                    var dates = shifts.Select(shift => (Start: input.WeekStart.ToDateTime(TimeOnly.MinValue).AddHours(shift.AbsoluteStart),
                        Finish: input.WeekStart.ToDateTime(TimeOnly.MinValue).AddHours(shift.AbsoluteFinish), New: true)).ToList();
                    dates.AddRange((boundaryByEmployee.GetValueOrDefault(employee.Id) ?? []).Select(shift => (shift.Start, shift.Finish, false)));
                    var ordered = dates.OrderBy(shift => shift.Start).ToArray();
                    for (var i = 1; i < ordered.Length; i++)
                        if (ordered[i - 1].New || ordered[i].New)
                            score += Math.Max(0, input.Options.PreferredRestHours - (ordered[i].Start - ordered[i - 1].Finish).TotalHours) * 100 * input.Options.ShortBreakPenalty;
                }
            }
            foreach (var shift in selected)
            {
                var shape = shift.Length <= 8
                    ? (8 - shift.Length) * (long)input.Options.LongShiftBonus + Math.Max(0, 6 - shift.Length) * (long)input.Options.ShortShiftPenalty
                    : (shift.Length - 6) * (long)input.Options.LongShiftBonus + (shift.Length - 8) * (long)input.Options.ShortShiftPenalty;
                score += (shape + input.Options.DailyShiftCountPenalty) * 100;
                if (TargetHours(employeeById[shift.EmployeeId]) == 0)
                    score += shift.Length * (long)input.Options.TargetHoursWeight * PercentageScale;
            }
            if (percentages.Count > 1)
                score += Math.Pow(Math.Max(0, percentages.Max() - percentages.Min() - 30), 2) * 100 * input.Options.FairnessSpreadWeight;
            return score;
        }
    }

    private sealed record FairnessContext(RosterSolverOptions Options, double CommonPercentage,
        IReadOnlyDictionary<Guid, double> HistoryAdjustedPercentages)
    {
        public double EmployeeCost(Employee employee, int hours)
        {
            if (TargetHours(employee) <= 0) return 0;
            var percentage = hours * 100.0 / TargetHours(employee);
            var cost = Options.TargetHoursWeight * Math.Pow(percentage - CommonPercentage, 2) * 100;
            if (HistoryAdjustedPercentages.TryGetValue(employee.Id, out var adjusted))
                cost += Options.HistoryFairnessWeight * Math.Pow(percentage - adjusted, 2) * 100;
            return cost;
        }

        public static FairnessContext Create(RosterSolverInput input, Employee[] employees)
        {
            var positive = employees.Where(employee => TargetHours(employee) > 0).ToArray();
            var totalTargets = positive.Sum(employee => (long)TargetHours(employee));
            var common = totalTargets == 0 ? 0 : input.Demand.Sum(slot => (long)slot.RequiredDrivers) * 100.0 / totalTargets;
            var employeeIds = positive.Select(employee => employee.Id).ToHashSet();
            var history = (input.History ?? []).Where(item => employeeIds.Contains(item.EmployeeId) && item.TargetHours > 0 &&
                item.ScheduledHours >= 0 && item.WeekStart < input.WeekStart && item.WeekStart.DayNumber >= input.WeekStart.DayNumber - 28).ToArray();
            var pastTarget = history.Sum(item => (long)item.TargetHours);
            var pastPercentage = pastTarget == 0 ? 0 : history.Sum(item => (long)item.ScheduledHours) * 100.0 / pastTarget;
            var adjusted = history.GroupBy(item => item.EmployeeId).ToDictionary(group => group.Key, group =>
            {
                var ownPercentage = group.Sum(item => (long)item.ScheduledHours) * 100.0 / group.Sum(item => (long)item.TargetHours);
                var weeks = group.Select(item => item.WeekStart).Distinct().Count();
                return common + Math.Clamp((pastPercentage - ownPercentage) / weeks, -15, 15);
            });
            return new FairnessContext(input.Options, common, adjusted);
        }
    }

    private static IEnumerable<string> FairnessWarnings(RosterSolverInput input, IReadOnlyList<RosterSolverShift> shifts)
    {
        var scheduled = shifts.GroupBy(shift => shift.EmployeeId).ToDictionary(group => group.Key, group => group.Sum(shift => shift.DurationHours));
        var ratios = input.Employees.Where(employee => employee.IsActive && TargetHours(employee) > 0)
            .Select(employee => (Employee: employee, Percentage: scheduled.GetValueOrDefault(employee.Id) * 100.0 / TargetHours(employee))).ToArray();
        if (ratios.Length > 1)
        {
            var minimum = ratios.MinBy(item => item.Percentage);
            var maximum = ratios.MaxBy(item => item.Percentage);
            var spread = maximum.Percentage - minimum.Percentage;
            if (spread > 30.01)
                yield return $"Fairness warning: target utilization still spans {minimum.Percentage:F1}% ({Name(minimum.Employee)}) to {maximum.Percentage:F1}% ({Name(maximum.Employee)}), a {spread:F1}-percentage-point gap. Coverage remains exact. Availability, target differences, shift/rest rules, selected weights or the search limit can restrict further balancing; review this allocation before using it.";
        }
    }

    private static (int Start, int Finish) AvailabilityWindow(Shift shift)
    {
        // The demand template defines a business day as 06:00 through 05:00 next morning.
        var start = shift.StartTime.Hour < 6 ? shift.StartTime.Hour + 24 : shift.StartTime.Hour;
        var finish = shift.FinishTime.Hour;
        while (finish <= start) finish += 24;
        return (start, finish);
    }

    private static double GapHours(DateTime start, DateTime finish, DateTime otherStart, DateTime otherFinish) =>
        finish <= otherStart ? (otherStart - finish).TotalHours : otherFinish <= start ? (start - otherFinish).TotalHours : -1;
    private static int AbsoluteHour(DateOnly weekStart, DateOnly date, int hour) => (date.DayNumber - weekStart.DayNumber) * 24 + hour;
    private static string Name(Employee employee) => $"{employee.FirstName} {employee.LastName}".Trim();

    private static int TargetHours(Employee employee) => employee.DriverProfile?.TargetHours ?? 0;

    private static bool IsCarDriver(Employee employee) => employee.DriverProfile?.DriverType == DriverType.Car;
    private static string FormatSlot(RosterSolverDemand slot) => $"{slot.Date.ToString("dddd", CultureInfo.InvariantCulture)} {slot.Hour % 24:00}:00{(slot.Hour >= 24 ? " (+1 day)" : string.Empty)}";

    private sealed record Candidate(int Index, Guid EmployeeId, bool IsCar, DateOnly Date,
        int Start, int Finish, int AbsoluteStart, long PreviousBoundaryRestPenalty, long NextBoundaryRestPenalty)
    {
        public int Length => Finish - Start;
        public int AbsoluteFinish => AbsoluteStart + Length;
    }
    private sealed record DayVariables(Guid EmployeeId, DateOnly Date, BoolVar Present, IntVar Start,
        IntVar Finish, int EarliestStart, int LatestFinish, Candidate[] Options);
    private sealed record ModelState(CpModel Model, BoolVar[] Selected, List<(RosterSolverDemand Slot, IntVar Missing)> Missing);

    private sealed class ProgressReporter(Action<RosterSolverProgress>? callback)
    {
        private readonly object sync = new();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private double lastPulse = -2;
        public void Publish(string stage, int progress, string message)
        {
            lock (sync)
            {
                lastPulse = clock.Elapsed.TotalSeconds;
                // Observability must not crash native search or invalidate a successful solution.
                try { callback?.Invoke(new RosterSolverProgress(stage, progress, message)); } catch { }
            }
        }
        public void Pulse(string stage, int progress, string message)
        {
            lock (sync)
            {
                if (clock.Elapsed.TotalSeconds - lastPulse < 2) return;
                Publish(stage, progress, message);
            }
        }
    }

    private sealed class SearchCallback(ProgressReporter reporter, bool diagnostic, CancellationToken token) : CpSolverSolutionCallback
    {
        public volatile bool HasSolution;
        public override void OnSolutionCallback()
        {
            if (token.IsCancellationRequested) { StopSearch(); return; }
            if (!HasSolution && !diagnostic)
                reporter.Publish("optimizing", 45, "A complete roster covering exactly 100% of hourly demand was found. Improving its weighted fairness, shift lengths and rest.");
            HasSolution = true;
        }
    }
}
