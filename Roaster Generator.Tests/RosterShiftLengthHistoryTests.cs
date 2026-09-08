using System.Diagnostics;
using Roaster_Generator.Entities;
using Roaster_Generator.Enums;
using Roaster_Generator.Services;

namespace Roaster_Generator.Tests;

public sealed partial class RosterSolverTests
{
    [Fact]
    public void HistoricallyShortShiftsReceiveLongerShiftsWithoutWideningWeeklyHourFairness()
    {
        var employees = new[] { Driver(targetHours: 8), Driver(targetHours: 8) };
        var history = Enumerable.Range(1, 4).SelectMany(week => new[]
        {
            new RosterSolverHistory(employees[0].Id, Monday.AddDays(-7 * week), 24, 24, ShiftCount: 6),
            new RosterSolverHistory(employees[1].Id, Monday.AddDays(-7 * week), 24, 24, ShiftCount: 3)
        }).ToArray();
        var input = ShiftLengthHistoryInput(employees, history);

        var result = solver.Solve(input);

        AssertExactRoster(input, result);
        Assert.All(employees, employee => Assert.Equal(8, Hours(result, employee.Id)));
        Assert.Equal(8, AverageLength(result, employees[0].Id));
        Assert.Equal(4, AverageLength(result, employees[1].Id));

        var reversed = input with
        {
            History = history.Select(item => item with { ShiftCount = item.ShiftCount == 6 ? 3 : 6 }).ToArray()
        };
        var reversedResult = solver.Solve(reversed);

        AssertExactRoster(reversed, reversedResult);
        Assert.All(employees, employee => Assert.Equal(8, Hours(reversedResult, employee.Id)));
        Assert.Equal(4, AverageLength(reversedResult, employees[0].Id));
        Assert.Equal(8, AverageLength(reversedResult, employees[1].Id));
    }

    [Fact]
    public void HistoricalAverageWeightsEachShiftRatherThanEachWeek()
    {
        var employees = new[] { Driver(targetHours: 8), Driver(targetHours: 8) };
        // First driver: 32 / 7 = 4.57h per shift, NOT (8 + 4) / 2 = 6h.
        // Second driver: 32 / 6 = 5.33h. The first driver should get the long shift.
        var input = ShiftLengthHistoryInput(employees,
        [
            new(employees[0].Id, Monday.AddDays(-7), 8, 8, ShiftCount: 1),
            new(employees[0].Id, Monday.AddDays(-14), 24, 24, ShiftCount: 6),
            new(employees[1].Id, Monday.AddDays(-7), 32, 32, ShiftCount: 6)
        ]);

        var result = solver.Solve(input);

        AssertExactRoster(input, result);
        Assert.All(employees, employee => Assert.Equal(8, Hours(result, employee.Id)));
        Assert.True(AverageLength(result, employees[0].Id) > AverageLength(result, employees[1].Id));
    }

    [Fact]
    public void ShiftHistoryNeverExtendsAShiftOutsideAvailabilityToImproveItsAverage()
    {
        var employees = new[] { Driver(targetHours: 8), Driver(targetHours: 8) };
        var input = ShiftLengthHistoryInput(employees,
        [
            new(employees[0].Id, Monday.AddDays(-7), 24, 24, ShiftCount: 6),
            new(employees[1].Id, Monday.AddDays(-7), 24, 24, ShiftCount: 3)
        ]) with
        {
            Availability = [Available(employees[1], Monday, 12, 20),
                Available(employees[0], Monday.AddDays(1), 12, 16),
                Available(employees[0], Monday.AddDays(2), 12, 16)]
        };

        var result = solver.Solve(input);

        AssertExactRoster(input, result);
        Assert.Equal(4, AverageLength(result, employees[0].Id));
        Assert.Equal(8, AverageLength(result, employees[1].Id));
    }

    [Fact]
    public void ShiftHistoryCanBeTheOnlyEnabledPreferenceAndCanBeDisabledByAdmin()
    {
        var employee = Driver();
        var input = Input([employee], [Available(employee, Monday, 12, 18)], Demand(Monday, 12, 6),
            options: OnlyShiftHistoryPreference(100),
            history: [new(employee.Id, Monday.AddDays(-7), 24, 24, ShiftCount: 6)]);

        var result = solver.Solve(input);

        AssertExactRoster(input, result);
        Assert.Equal("optimal", result.Status);
        // One employee's historical average equals the group average, giving a 7h preference.
        Assert.Equal(10_000d, result.ObjectiveValue);

        var disabled = solver.Solve(input with { Options = OnlyShiftHistoryPreference(0) });
        AssertExactRoster(input, disabled);
        Assert.Equal(0d, disabled.ObjectiveValue);
    }

    [Theory]
    [InlineData(-35)]
    [InlineData(0)]
    [InlineData(7)]
    public void ShiftHistoryIgnoresWeeksOutsideThePreviousFour(int offsetDays)
    {
        var employee = Driver();
        var input = Input([employee], [Available(employee, Monday, 12, 18)], Demand(Monday, 12, 6),
            options: OnlyShiftHistoryPreference(100),
            history: [new(employee.Id, Monday.AddDays(offsetDays), 24, 24, ShiftCount: 6)]);

        var result = solver.Solve(input);

        AssertExactRoster(input, result);
        Assert.Equal(0d, result.ObjectiveValue);
    }

    [Fact]
    public void UnknownAndZeroHistoricalShiftCountsDoNotInventAnAverage()
    {
        var employee = Driver();
        var input = Input([employee], [Available(employee, Monday, 12, 18)], Demand(Monday, 12, 6),
            options: OnlyShiftHistoryPreference(100), history:
            [new(employee.Id, Monday.AddDays(-7), 24, 24),
             new(employee.Id, Monday.AddDays(-14), 0, 24, ShiftCount: 0)]);

        var result = solver.Solve(input);

        AssertExactRoster(input, result);
        Assert.Equal(0d, result.ObjectiveValue);
    }

    [Fact]
    public void FullWeekWithMixedDriverTypesAndShiftHistoryFitsAShortServerBudget()
    {
        var employees = Enumerable.Range(0, 20).Select(index => Driver(targetHours: 14,
            driverType: index % 3 == 0 ? DriverType.EBike : index % 3 == 1 ? DriverType.Moped : DriverType.Car)).ToArray();
        var dates = Enumerable.Range(0, 7).Select(Monday.AddDays).ToArray();
        var history = Enumerable.Range(1, 4).SelectMany(week => employees.Select((employee, index) =>
            new RosterSolverHistory(employee.Id, Monday.AddDays(-7 * week), 24, 24, ShiftCount: index % 2 == 0 ? 6 : 3))).ToArray();
        var input = Input(employees,
            dates.SelectMany(date => employees.Select(employee => Available(employee, date, 12, 20))).ToArray(),
            dates.SelectMany(date => Demand(date, 12, 8, requiredDrivers: 5)).ToArray(),
            options: new RosterSolverOptions { MaxSolveSeconds = 3 }, history: history);
        var watch = Stopwatch.StartNew();

        var result = solver.Solve(input);

        watch.Stop();
        AssertExactRoster(input, result);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(6), $"Three-second solve budget took {watch.Elapsed}.");
        output.WriteLine($"20 mixed drivers, seven days, four history weeks: {result.Status}, " +
            $"{result.CandidateCount:N0} candidates, {watch.Elapsed.TotalSeconds:F3}s.");
    }

    private static RosterSolverInput ShiftLengthHistoryInput(Employee[] employees, IReadOnlyList<RosterSolverHistory> history) =>
        Input(employees, employees.SelectMany(employee => new[]
        {
            Available(employee, Monday, 12, 20), Available(employee, Monday.AddDays(1), 12, 16),
            Available(employee, Monday.AddDays(2), 12, 16)
        }).ToArray(), [.. Demand(Monday, 12, 8), .. Demand(Monday.AddDays(1), 12, 4), .. Demand(Monday.AddDays(2), 12, 4)],
        history: history);

    private static double AverageLength(RosterSolverResult result, Guid employeeId) =>
        result.Shifts.Where(shift => shift.EmployeeId == employeeId).Average(shift => shift.DurationHours);

    private static RosterSolverOptions OnlyShiftHistoryPreference(int weight) => new()
    {
        TargetHoursWeight = 0, HistoryFairnessWeight = 0, HistoryShiftLengthWeight = weight,
        FairnessSpreadWeight = 0, LongShiftBonus = 0, ShortShiftPenalty = 0,
        DailyShiftCountPenalty = 0, ShortBreakPenalty = 0, MaxSolveSeconds = 2
    };
}
