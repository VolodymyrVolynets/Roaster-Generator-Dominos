using System.Diagnostics;
using Roaster_Generator.Entities;
using Roaster_Generator.Services;
using Xunit.Abstractions;

namespace Roaster_Generator.Tests;

public sealed partial class RosterSolverTests(ITestOutputHelper output)
{
    private static readonly DateOnly Monday = new(2026, 9, 7);
    private readonly RosterSolver solver = new();

    [Theory]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(10)]
    public void CoversDemandExactlyAtSupportedShiftLengths(int duration)
    {
        var employee = Driver();
        var input = Input([employee], [Available(employee, Monday, 9, 21)], Demand(Monday, 10, duration));

        var result = solver.Solve(input);

        AssertExactRoster(input, result);
        var shift = Assert.Single(result.Shifts);
        Assert.Equal(10, shift.StartHour);
        Assert.Equal(10 + duration, shift.FinishHour);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(11)]
    public void RefusesDemandRequiringAnIllegalShiftLength(int duration)
    {
        var employee = Driver();
        var input = Input([employee], [Available(employee, Monday, 9, 23)], Demand(Monday, 10, duration));

        var result = solver.Solve(input);

        AssertInfeasible(result);
    }

    [Fact]
    public void DoesNotBridgeUnspecifiedOrZeroDemandHours()
    {
        var employees = new[] { Driver(), Driver() };
        var input = Input(employees,
            employees.Select(employee => Available(employee, Monday, 9, 21)).ToArray(),
            [.. Demand(Monday, 10, 3), new(Monday, 13, 0), .. Demand(Monday, 16, 3)]);

        var result = solver.Solve(input);

        AssertExactRoster(input, result);
        Assert.Equal(2, result.Shifts.Count);
        Assert.All(result.Shifts, shift => Assert.Equal(3, shift.DurationHours));
        Assert.Equal(2, result.Shifts.Select(shift => shift.EmployeeId).Distinct().Count());
    }

    [Fact]
    public void ExactlyCoversChangingHourlyDemand()
    {
        var employees = new[] { Driver(), Driver() };
        var profile = new[] { 1, 1, 2, 2, 2, 2, 1, 1 };
        var input = Input(employees,
            employees.Select(employee => Available(employee, Monday, 12, 20)).ToArray(),
            profile.Select((required, offset) => new RosterSolverDemand(Monday, 12 + offset, required)).ToArray());

        AssertExactRoster(input, solver.Solve(input));
    }

    [Fact]
    public void NeverAssignsTwoSeparateShiftsToTheSameEmployeeOnOneDay()
    {
        var employee = Driver();
        var input = Input([employee], [Available(employee, Monday, 9, 21)],
            [.. Demand(Monday, 10, 3), .. Demand(Monday, 16, 3)]);

        AssertInfeasible(solver.Solve(input));
    }

    [Fact]
    public void EqualizesThePercentageOfDifferentTargetHours()
    {
        var lowTarget = Driver(targetHours: 20);
        var highTarget = Driver(targetHours: 40);
        var employees = new[] { lowTarget, highTarget };
        var dates = Enumerable.Range(0, 3).Select(Monday.AddDays).ToArray();
        var input = Input(employees,
            dates.SelectMany(date => employees.Select(employee => Available(employee, date, 12, 18))).ToArray(),
            dates.SelectMany(date => Demand(date, 12, 6)).ToArray(),
            options: new RosterSolverOptions
            {
                TargetHoursWeight = 1000,
                LongShiftBonus = 0,
                ShortShiftPenalty = 0,
                DailyShiftCountPenalty = 0,
                ShortBreakPenalty = 0,
                MaxSolveSeconds = 2
            });

        var result = solver.Solve(input);

        AssertExactRoster(input, result);
        Assert.Equal(6, result.Shifts.Where(shift => shift.EmployeeId == lowTarget.Id).Sum(shift => shift.DurationHours));
        Assert.Equal(12, result.Shifts.Where(shift => shift.EmployeeId == highTarget.Id).Sum(shift => shift.DurationHours));
    }

    [Fact]
    public void KeepsAvailableEmployeesBalancedWhenAnotherEmployeeCannotWork()
    {
        var first = Driver();
        var second = Driver();
        var unavailable = Driver();
        var dates = new[] { Monday, Monday.AddDays(1) };
        var input = Input([first, second, unavailable],
            dates.SelectMany(date => new[] { first, second }.Select(employee => Available(employee, date, 12, 22))).ToArray(),
            dates.SelectMany(date => Demand(date, 12, 10)).ToArray(),
            options: new RosterSolverOptions
            {
                TargetHoursWeight = 1000,
                LongShiftBonus = 0,
                ShortShiftPenalty = 0,
                DailyShiftCountPenalty = 0,
                ShortBreakPenalty = 0,
                MaxSolveSeconds = 2
            });

        var result = solver.Solve(input);

        AssertExactRoster(input, result);
        Assert.Equal(10, result.Shifts.Where(shift => shift.EmployeeId == first.Id).Sum(shift => shift.DurationHours));
        Assert.Equal(10, result.Shifts.Where(shift => shift.EmployeeId == second.Id).Sum(shift => shift.DurationHours));
        Assert.DoesNotContain(result.Shifts, shift => shift.EmployeeId == unavailable.Id);
    }

    [Fact]
    public void ConfiguredShiftPreferenceAvoidsSplittingAnEightHourShift()
    {
        var employees = new[] { Driver(), Driver(), Driver() };
        var input = Input(employees,
            employees.Select(employee => Available(employee, Monday, 12, 20)).ToArray(), Demand(Monday, 12, 8),
            options: new RosterSolverOptions
            {
                TargetHoursWeight = 0,
                HistoryFairnessWeight = 0,
                FairnessSpreadWeight = 0,
                LongShiftBonus = 1000,
                ShortShiftPenalty = 1000,
                DailyShiftCountPenalty = 1000,
                ShortBreakPenalty = 0,
                MaxSolveSeconds = 2
            });

        var result = solver.Solve(input);

        AssertExactRoster(input, result);
        Assert.Equal(8, Assert.Single(result.Shifts).DurationHours);
    }

    [Fact]
    public void ConfiguredRestPreferenceChoosesTheBetterRestedEmployee()
    {
        var nightDriver = Driver();
        var restedDriver = Driver();
        var tuesday = Monday.AddDays(1);
        var input = Input([nightDriver, restedDriver],
            [Available(nightDriver, Monday, 20, 2), Available(nightDriver, tuesday, 10, 16), Available(restedDriver, tuesday, 10, 16)],
            [.. Demand(Monday, 20, 6), .. Demand(tuesday, 10, 6)],
            options: new RosterSolverOptions
            {
                TargetHoursWeight = 0,
                HistoryFairnessWeight = 0,
                FairnessSpreadWeight = 0,
                LongShiftBonus = 0,
                ShortShiftPenalty = 0,
                DailyShiftCountPenalty = 0,
                ShortBreakPenalty = 1000,
                MinimumRestHours = 8,
                PreferredRestHours = 12,
                MaxSolveSeconds = 2
            });

        var result = solver.Solve(input);

        AssertExactRoster(input, result);
        Assert.Equal(restedDriver.Id, Assert.Single(result.Shifts, shift => shift.Date == tuesday).EmployeeId);
    }

    [Fact]
    public void AdminCanIncreaseTheMinimumRestRequirement()
    {
        var employee = Driver();
        var tuesday = Monday.AddDays(1);
        var input = Input([employee],
            [Available(employee, Monday, 20, 2), Available(employee, tuesday, 11, 17)],
            [.. Demand(Monday, 20, 6), .. Demand(tuesday, 11, 6)],
            options: new RosterSolverOptions { MinimumRestHours = 10, PreferredRestHours = 12, MaxSolveSeconds = 2 });

        AssertInfeasible(solver.Solve(input));
    }

    [Fact]
    public void RestPreferenceMeasuresOnlyActualConsecutiveBreaksIncludingSavedWeeks()
    {
        var employee = Driver();
        var dates = Enumerable.Range(0, 7).Select(Monday.AddDays).ToArray();
        var boundaries = new[] { Monday.AddDays(-1), Monday.AddDays(7) }
            .Select(date => new RosterSolverBoundaryShift(employee.Id,
                date.ToDateTime(new TimeOnly(12, 0)), date.ToDateTime(new TimeOnly(18, 0)))).ToArray();
        var input = Input([employee], dates.Select(date => Available(employee, date, 12, 18)).ToArray(),
            dates.SelectMany(date => Demand(date, 12, 6)).ToArray(), boundaries,
            new RosterSolverOptions
            {
                TargetHoursWeight = 0,
                HistoryFairnessWeight = 0,
                FairnessSpreadWeight = 0,
                LongShiftBonus = 0,
                ShortShiftPenalty = 0,
                DailyShiftCountPenalty = 0,
                ShortBreakPenalty = 100,
                PreferredRestHours = 48,
                MaxSolveSeconds = 2
            });

        var result = solver.Solve(input);

        AssertExactRoster(input, result);
        // Nine consecutive working days, including the saved boundaries, have eight 18-hour breaks.
        Assert.Equal(8 * (48 - 18) * 100 * 100, result.ObjectiveValue);
    }

    [Fact]
    public void CoversDemandUsingZeroTargetEmployeeWithoutDivisionByZero()
    {
        var employee = Driver(targetHours: 0);
        var input = Input([employee], [Available(employee, Monday, 12, 18)], Demand(Monday, 12, 6));

        var result = solver.Solve(input);

        AssertExactRoster(input, result);
        Assert.True(double.IsFinite(result.TargetUtilizationPercent));
    }

    [Fact]
    public void DoesNotScheduleAnInactiveEmployee()
    {
        var employee = Driver();
        employee.IsActive = false;
        var input = Input([employee], [Available(employee, Monday, 12, 18)], Demand(Monday, 12, 6));

        AssertInfeasible(solver.Solve(input));
    }

    [Fact]
    public void DoesNotScheduleAnEmployeeWhoHasNoAvailability()
    {
        var employee = Driver();

        AssertInfeasible(solver.Solve(Input([employee], [], Demand(Monday, 12, 6))));
    }

    [Fact]
    public void RequiresAQualifiedEmployeeDuringEveryDemandHour()
    {
        var employee = Driver(canWorkAlone: false);
        var input = Input([employee], [Available(employee, Monday, 12, 18)], Demand(Monday, 12, 6));

        var result = solver.Solve(input);

        AssertInfeasible(result);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Contains("alone", StringComparison.OrdinalIgnoreCase) ||
            diagnostic.Contains("qualified", StringComparison.OrdinalIgnoreCase) ||
            diagnostic.Contains("supervis", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AllowsUnqualifiedEmployeeAlongsideAQualifiedEmployee()
    {
        var employees = new[] { Driver(), Driver(canWorkAlone: false) };
        var input = Input(employees,
            employees.Select(employee => Available(employee, Monday, 12, 18)).ToArray(),
            Demand(Monday, 12, 6, requiredDrivers: 2));

        AssertExactRoster(input, solver.Solve(input));
    }

    [Fact]
    public void ShortageDiagnosticIdentifiesSundayAndTheHour()
    {
        var sunday = Monday.AddDays(6);
        var employee = Driver();
        var input = Input([employee], [Available(employee, sunday, 13, 19)],
            Demand(sunday, 13, 6, requiredDrivers: 2));

        var result = solver.Solve(input);

        AssertInfeasible(result);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Contains("Sunday", StringComparison.OrdinalIgnoreCase) &&
            diagnostic.Contains("13:00", StringComparison.Ordinal) &&
            diagnostic.Contains("1", StringComparison.Ordinal) &&
            diagnostic.Contains("driver", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RetainsBusinessDateAndExtendedHoursForAnOvernightShift()
    {
        var employee = Driver();
        var input = Input([employee], [Available(employee, Monday, 21, 3)], Demand(Monday, 21, 6));

        var result = solver.Solve(input);

        AssertExactRoster(input, result);
        var shift = Assert.Single(result.Shifts);
        Assert.Equal(Monday, shift.Date);
        Assert.Equal(21, shift.StartHour);
        Assert.Equal(27, shift.FinishHour);
    }

    [Fact]
    public void AvailabilityStartingAfterMidnightCannotBypassTheLatestShiftStart()
    {
        var employee = Driver();
        var input = Input([employee], [Available(employee, Monday, 1, 4)], Demand(Monday, 25, 3));

        var result = solver.Solve(input);

        AssertInfeasible(result);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("22:00", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(10, false)]
    [InlineData(11, true)]
    public void EnforcesRestAfterAnOvernightShift(int secondStartHour, bool expectedSuccess)
    {
        var employee = Driver();
        var tuesday = Monday.AddDays(1);
        var input = Input([employee],
            [Available(employee, Monday, 21, 3), Available(employee, tuesday, secondStartHour, secondStartHour + 6)],
            [.. Demand(Monday, 21, 6), .. Demand(tuesday, secondStartHour, 6)]);

        var result = solver.Solve(input);

        if (expectedSuccess) AssertExactRoster(input, result);
        else AssertInfeasible(result);
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(6, false)]
    public void EnforcesRestAfterAShiftSavedInThePreviousWeek(int previousFinishHour, bool expectedSuccess)
    {
        var employee = Driver();
        var boundary = new RosterSolverBoundaryShift(employee.Id,
            Monday.AddDays(-1).ToDateTime(new TimeOnly(21, 0)),
            Monday.ToDateTime(new TimeOnly(previousFinishHour, 0)));
        var input = Input([employee], [Available(employee, Monday, 13, 19)], Demand(Monday, 13, 6), [boundary]);

        var result = solver.Solve(input);

        if (expectedSuccess) AssertExactRoster(input, result);
        else AssertInfeasible(result);
    }

    [Fact]
    public void EnforcesRestBeforeAShiftSavedInTheNextWeek()
    {
        var employee = Driver();
        var sunday = Monday.AddDays(6);
        var nextMonday = Monday.AddDays(7);
        var boundary = new RosterSolverBoundaryShift(employee.Id,
            nextMonday.ToDateTime(new TimeOnly(3, 0)), nextMonday.ToDateTime(new TimeOnly(9, 0)));
        var input = Input([employee], [Available(employee, sunday, 18, 22)], Demand(sunday, 18, 4), [boundary]);

        AssertInfeasible(solver.Solve(input));
    }

    [Fact]
    public void RejectsConflictingDuplicateAvailabilityRows()
    {
        var employee = Driver();
        var input = Input([employee], [Available(employee, Monday, 10, 16), Available(employee, Monday, 13, 19)],
            Demand(Monday, 12, 3));

        var result = solver.Solve(input);

        Assert.False(result.Success);
        Assert.Equal("invalid", result.Status);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public void RejectsNonHourlyAvailabilityInsteadOfRoundingSilently()
    {
        var employee = Driver();
        var availability = Available(employee, Monday, 12, 18);
        availability.StartTime = new TimeOnly(12, 30);

        var result = solver.Solve(Input([employee], [availability], Demand(Monday, 12, 6)));

        Assert.Equal("invalid", result.Status);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(48)]
    public void RejectsDemandHoursOutsideTheBusinessTimeline(int hour)
    {
        var employee = Driver();

        var result = solver.Solve(Input([employee], [Available(employee, Monday, 12, 18)], [new(Monday, hour, 1)]));

        Assert.Equal("invalid", result.Status);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public void EmptyDemandProducesAnEmptyValidRoster()
    {
        var input = Input([], [], []);

        var result = solver.Solve(input);

        AssertExactRoster(input, result);
        Assert.Empty(result.Shifts);
    }

    [Fact]
    public void CancellationStopsBeforeBuildingTheModel()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => solver.Solve(Input([], [], []),
            cancellationToken: cancellation.Token));
    }

    [Fact]
    public void CancellationStopsAnActiveSolvePromptly()
    {
        var employees = Enumerable.Range(0, 20).Select(_ => Driver(targetHours: 28)).ToArray();
        var dates = Enumerable.Range(0, 7).Select(Monday.AddDays).ToArray();
        var input = Input(employees,
            dates.SelectMany(date => employees.Select(employee => Available(employee, date, 12, 20))).ToArray(),
            dates.SelectMany(date => Demand(date, 12, 8, requiredDrivers: 10)).ToArray());
        using var cancellation = new CancellationTokenSource();
        var watch = Stopwatch.StartNew();

        Assert.ThrowsAny<OperationCanceledException>(() => solver.Solve(input, progress =>
        {
            if (progress.Stage == "solving") cancellation.CancelAfter(TimeSpan.FromMilliseconds(10));
        }, cancellation.Token));

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"Cancellation took {watch.Elapsed}.");
    }

    [Fact]
    public void ADisconnectedProgressObserverDoesNotInvalidateTheRoster()
    {
        var employee = Driver();
        var input = Input([employee], [Available(employee, Monday, 12, 18)], Demand(Monday, 12, 6));

        var result = solver.Solve(input, _ => throw new IOException("The log connection disconnected."));

        AssertExactRoster(input, result);
    }

    [Fact]
    public void ReportsCapacityLimitWithoutClaimingDemandIsImpossible()
    {
        var employees = Enumerable.Range(0, 1001).Select(_ => Driver()).ToArray();

        var result = solver.Solve(Input(employees, [], Demand(Monday, 12, 6)));

        Assert.False(result.Success);
        Assert.Equal("capacity-exceeded", result.Status);
        Assert.NotEmpty(result.Diagnostics);
        Assert.Empty(result.Shifts);
    }

    [Fact]
    public void RejectsDifferentBusinessDateLabelsForTheSameRealHour()
    {
        var employee = Driver();
        var input = Input([employee], [], [new(Monday, 24, 1), new(Monday.AddDays(1), 0, 1)]);

        var result = solver.Solve(input);

        Assert.Equal("invalid", result.Status);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public void ProgressProvidesReadableMessagesAndValidPercentages()
    {
        var employee = Driver();
        var input = Input([employee], [Available(employee, Monday, 12, 18)], Demand(Monday, 12, 6));
        var progress = new List<RosterSolverProgress>();

        var result = solver.Solve(input, progress.Add);

        AssertExactRoster(input, result);
        Assert.NotEmpty(progress);
        Assert.All(progress, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Stage));
            Assert.False(string.IsNullOrWhiteSpace(entry.Message));
            Assert.InRange(entry.Progress, 0, 100);
        });
    }

    [Fact]
    public void IndependentValidationRejectsUnderAndOverCoverage()
    {
        var employees = new[] { Driver(), Driver() };
        var input = Input(employees,
            employees.Select(employee => Available(employee, Monday, 12, 18)).ToArray(), Demand(Monday, 12, 6));

        Assert.NotEmpty(RosterSolver.Validate(input, []));
        Assert.NotEmpty(RosterSolver.Validate(input,
            employees.Select(employee => new RosterSolverShift(employee.Id, Monday, 12, 18)).ToArray()));
        Assert.NotEmpty(RosterSolver.Validate(input, [new(employees[0].Id, Monday, 12, 19)]));
    }

    [Fact]
    public void IndependentValidationRejectsCoverageOutsideEmployeeAvailability()
    {
        var employee = Driver();
        var input = Input([employee], [Available(employee, Monday, 13, 19)], Demand(Monday, 12, 6));

        Assert.NotEmpty(RosterSolver.Validate(input, [new(employee.Id, Monday, 12, 18)]));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(11)]
    public void IndependentValidationRejectsIllegalShiftLengthsEvenWithExactCoverage(int duration)
    {
        var employee = Driver();
        var input = Input([employee], [Available(employee, Monday, 10, 23)], Demand(Monday, 10, duration));

        Assert.NotEmpty(RosterSolver.Validate(input, [new(employee.Id, Monday, 10, 10 + duration)]));
    }

    [Fact]
    public void IndependentValidationRejectsAnUnknownEmployee()
    {
        var employee = Driver();
        var input = Input([employee], [Available(employee, Monday, 12, 18)], Demand(Monday, 12, 6));

        Assert.NotEmpty(RosterSolver.Validate(input, [new(Guid.NewGuid(), Monday, 12, 18)]));
    }

    [Fact]
    public void IndependentValidationRejectsInadequateRestEvenWithExactCoverage()
    {
        var employee = Driver();
        var tuesday = Monday.AddDays(1);
        var input = Input([employee], [Available(employee, Monday, 21, 3), Available(employee, tuesday, 10, 16)],
            [.. Demand(Monday, 21, 6), .. Demand(tuesday, 10, 6)]);

        Assert.NotEmpty(RosterSolver.Validate(input,
            [new(employee.Id, Monday, 21, 27), new(employee.Id, tuesday, 10, 16)]));
    }

    [Fact]
    public void IndependentValidationRejectsUnsupervisedCoverage()
    {
        var employee = Driver(canWorkAlone: false);
        var input = Input([employee], [Available(employee, Monday, 12, 18)], Demand(Monday, 12, 6));

        Assert.NotEmpty(RosterSolver.Validate(input, [new(employee.Id, Monday, 12, 18)]));
    }

    [Fact]
    public void TwentyEmployeesAndAFullWeekRespectTheServerTimeBudget()
    {
        var employees = Enumerable.Range(0, 20).Select(_ => Driver(targetHours: 28)).ToArray();
        var dates = Enumerable.Range(0, 7).Select(Monday.AddDays).ToArray();
        var input = Input(employees,
            dates.SelectMany(date => employees.Select(employee => Available(employee, date, 12, 20))).ToArray(),
            dates.SelectMany(date => Demand(date, 12, 8, requiredDrivers: 10)).ToArray(),
            options: new RosterSolverOptions { MaxSolveSeconds = 2 });
        var watch = Stopwatch.StartNew();

        var result = solver.Solve(input);

        watch.Stop();
        output.WriteLine($"20 employees / 7 days: {result.Status}, {result.CandidateCount:N0} candidates, " +
            $"{watch.Elapsed.TotalSeconds:F3}s total, {result.WallTimeSeconds:F3}s reported.");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15), $"Bounded solve took {watch.Elapsed}.");
        // A small server budget may expire before any solution; that is not a proof of infeasibility.
        Assert.Contains(result.Status, new[] { "optimal", "feasible", "timed-out" });
        if (result.Success) AssertExactRoster(input, result);
        else Assert.Empty(result.Shifts);
    }

    [Fact]
    public void FindsExactCoverageForALongOpeningWeekWithinThreeSeconds()
    {
        var employees = Enumerable.Range(0, 20).Select(_ => Driver()).ToArray();
        var dates = Enumerable.Range(0, 7).Select(Monday.AddDays).ToArray();
        var input = Input(employees,
            dates.SelectMany(date => employees.Select(employee => Available(employee, date, 12,
                date.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday ? 4 : 1))).ToArray(),
            dates.SelectMany(date => Demand(date, 12,
                date.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday ? 16 : 13)).ToArray(),
            options: new RosterSolverOptions { MaxSolveSeconds = 3 });
        var watch = Stopwatch.StartNew();

        var result = solver.Solve(input);

        watch.Stop();
        output.WriteLine($"20 employees / 97 demand hours: {result.Status}, {result.CandidateCount:N0} candidates, " +
            $"{watch.Elapsed.TotalSeconds:F3}s total, {result.WallTimeSeconds:F3}s reported.");
        output.WriteLine($"Objective: {result.ObjectiveValue?.ToString("F0") ?? "not optimized"}; {result.Message}");
        output.WriteLine("Allocated hours (ascending): " + string.Join(", ", employees.Select(employee =>
            result.Shifts.Where(shift => shift.EmployeeId == employee.Id).Sum(shift => shift.DurationHours)).Order()));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(6), $"Bounded solve took {watch.Elapsed}.");
        AssertExactRoster(input, result);
        Assert.Equal(97, result.TotalScheduledHours);
        var allocations = employees.Select(employee =>
            result.Shifts.Where(shift => shift.EmployeeId == employee.Id).Sum(shift => shift.DurationHours)).ToArray();
        Assert.True(allocations.Max() - allocations.Min() <= 6,
            $"The current allocation exceeds a 30-point target percentage gap: {string.Join(", ", allocations.Order())}.");
    }

    private static Employee Driver(int targetHours = 20, bool canWorkAlone = true) => new()
    {
        Id = Guid.NewGuid(),
        FirstName = "Test",
        LastName = "Driver",
        TargetHours = targetHours,
        CanWorkAlone = canWorkAlone
    };

    private static Shift Available(Employee employee, DateOnly date, int start, int finish) => new()
    {
        Id = Guid.NewGuid(),
        EmployeeId = employee.Id,
        Employee = employee,
        Date = date,
        StartTime = new TimeOnly(start, 0),
        FinishTime = new TimeOnly(finish, 0)
    };

    private static RosterSolverDemand[] Demand(DateOnly date, int start, int duration, int requiredDrivers = 1) =>
        Enumerable.Range(start, duration).Select(hour => new RosterSolverDemand(date, hour, requiredDrivers)).ToArray();

    private static RosterSolverInput Input(
        IReadOnlyList<Employee> employees,
        IReadOnlyList<Shift> availability,
        IReadOnlyList<RosterSolverDemand> demand,
        IReadOnlyList<RosterSolverBoundaryShift>? boundaries = null,
        RosterSolverOptions? options = null,
        IReadOnlyList<RosterSolverHistory>? history = null) =>
        new(Monday, employees, availability, demand, boundaries ?? [],
            options ?? new RosterSolverOptions { MaxSolveSeconds = 2 }, history);

    private static void AssertInfeasible(RosterSolverResult result)
    {
        Assert.False(result.Success);
        Assert.Equal("infeasible", result.Status);
        Assert.Empty(result.Shifts);
        Assert.NotEmpty(result.Diagnostics);
    }

    private static void AssertExactRoster(RosterSolverInput input, RosterSolverResult result)
    {
        Assert.True(result.Success, $"{result.Status}: {result.Message}\n{string.Join('\n', result.Diagnostics)}");
        Assert.Empty(RosterSolver.Validate(input, result.Shifts));
        Assert.Equal(input.Demand.Sum(slot => slot.RequiredDrivers), result.TotalDemandHours);
        Assert.Equal(result.TotalDemandHours, result.TotalScheduledHours);
        Assert.Equal(result.TotalScheduledHours, result.Shifts.Sum(shift => shift.DurationHours));
        Assert.All(result.Shifts, shift => Assert.InRange(shift.DurationHours, 3, 10));
        Assert.All(result.Shifts, shift => Assert.InRange(shift.StartHour, 6, 22));
        Assert.All(result.Shifts.GroupBy(shift => (shift.EmployeeId, shift.Date)), group => Assert.Single(group));
        var demand = input.Demand.ToDictionary(slot => (slot.Date, slot.Hour), slot => slot.RequiredDrivers);
        for (var day = 0; day < 7; day++)
        for (var hour = 0; hour < 48; hour++)
        {
            var date = input.WeekStart.AddDays(day);
            var required = demand.GetValueOrDefault((date, hour));
            var assigned = result.Shifts.Where(shift => shift.Date == date && shift.StartHour <= hour && shift.FinishHour > hour).ToArray();
            Assert.Equal(required, assigned.Length);
            if (required > 0)
                Assert.Contains(assigned, shift => input.Employees.Single(employee => employee.Id == shift.EmployeeId).CanWorkAlone);
        }
    }
}
