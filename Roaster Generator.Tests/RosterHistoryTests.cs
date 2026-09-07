using Roaster_Generator.Services;

namespace Roaster_Generator.Tests;

public sealed partial class RosterSolverTests
{
    [Fact]
    public void ExistingInputsWithoutTheOptionalHistoryRemainValid()
    {
        var employee = Driver();
        var input = new RosterSolverInput(Monday, [employee], [Available(employee, Monday, 12, 18)],
            Demand(Monday, 12, 6), [], new RosterSolverOptions { MaxSolveSeconds = 2 });

        Assert.Null(input.History);
        AssertExactRoster(input, solver.Solve(input));
    }

    [Fact]
    public void PreviousFourWeeksCompensateUnderAllocationWhileKeepingTheCurrentGapSmall()
    {
        var overAllocated = Driver();
        var underAllocated = Driver();
        var employees = new[] { overAllocated, underAllocated };
        var dates = Enumerable.Range(0, 4).Select(Monday.AddDays).ToArray();
        var history = Enumerable.Range(1, 4).SelectMany(week => new[]
        {
            new RosterSolverHistory(overAllocated.Id, Monday.AddDays(-7 * week), 28, 20),
            new RosterSolverHistory(underAllocated.Id, Monday.AddDays(-7 * week), 12, 20)
        }).ToArray();
        var input = Input(employees,
            dates.SelectMany(date => employees.Select(employee => Available(employee, date, 12, 22))).ToArray(),
            dates.SelectMany(date => Demand(date, 12, 10)).ToArray(), history: history);

        var result = solver.Solve(input);

        AssertExactRoster(input, result);
        var highHistoryHours = Hours(result, overAllocated.Id);
        var lowHistoryHours = Hours(result, underAllocated.Id);
        Assert.True(lowHistoryHours > highHistoryHours,
            $"Historical under-allocation should receive compensation; allocated {highHistoryHours}/{lowHistoryHours} hours.");
        Assert.True((lowHistoryHours - highHistoryHours) * 5 <= 30,
            $"Compensation must keep this week's percentage gap small; allocated {highHistoryHours}/{lowHistoryHours} hours.");

        var reversed = solver.Solve(input with
        {
            History = history.Select(item => item with { ScheduledHours = item.ScheduledHours == 28 ? 12 : 28 }).ToArray()
        });

        AssertExactRoster(input, reversed);
        Assert.True(Hours(reversed, overAllocated.Id) > Hours(reversed, underAllocated.Id),
            "Reversing the prior allocations must reverse which employee receives compensation.");
    }

    [Theory]
    [InlineData(-35)]
    [InlineData(0)]
    [InlineData(7)]
    public void HistoryOutsideThePreviousFourWeeksDoesNotChangeAllocation(int offsetDays)
    {
        var employees = new[] { Driver(), Driver() };
        var dates = Enumerable.Range(0, 4).Select(Monday.AddDays).ToArray();
        var input = Input(employees,
            dates.SelectMany(date => employees.Select(employee => Available(employee, date, 12, 22))).ToArray(),
            dates.SelectMany(date => Demand(date, 12, 10)).ToArray(), history:
            [new(employees[0].Id, Monday.AddDays(offsetDays), 40, 20), new(employees[1].Id, Monday.AddDays(offsetDays), 0, 20)]);

        var result = solver.Solve(input);

        AssertExactRoster(input, result);
        Assert.Equal(20, Hours(result, employees[0].Id));
        Assert.Equal(20, Hours(result, employees[1].Id));
    }

    [Fact]
    public void NewEmployeeWithoutHistoryStillReceivesAFairCurrentAllocation()
    {
        var existing = Driver();
        var newEmployee = Driver();
        var employees = new[] { existing, newEmployee };
        var dates = new[] { Monday, Monday.AddDays(1) };
        var history = Enumerable.Range(1, 4)
            .Select(week => new RosterSolverHistory(existing.Id, Monday.AddDays(-7 * week), 20, 20)).ToArray();
        var input = Input(employees,
            dates.SelectMany(date => employees.Select(employee => Available(employee, date, 12, 22))).ToArray(),
            dates.SelectMany(date => Demand(date, 12, 10)).ToArray(), history: history);

        var result = solver.Solve(input);

        AssertExactRoster(input, result);
        Assert.InRange(Hours(result, newEmployee.Id), 7, 13);
        Assert.True(Math.Abs(Hours(result, existing.Id) - Hours(result, newEmployee.Id)) * 5 <= 30);
    }

    [Fact]
    public void ZeroTargetHistoryDoesNotProduceUndefinedPercentages()
    {
        var employees = new[] { Driver(), Driver() };
        var input = Input(employees,
            employees.Select(employee => Available(employee, Monday, 12, 18)).ToArray(), Demand(Monday, 12, 6), history:
            [new(employees[0].Id, Monday.AddDays(-7), 12, 0), new(employees[1].Id, Monday.AddDays(-7), 24, 20)]);

        var result = solver.Solve(input);

        AssertExactRoster(input, result);
        Assert.True(double.IsFinite(result.TargetUtilizationPercent));
        Assert.True(result.ObjectiveValue is null || double.IsFinite(result.ObjectiveValue.Value));
    }

    [Fact]
    public void DefaultFairnessPreventsALargePercentageGapEvenWhenOneLongShiftWouldFit()
    {
        var employees = new[] { Driver(targetHours: 10), Driver(targetHours: 10) };
        var input = Input(employees,
            employees.Select(employee => Available(employee, Monday, 12, 19)).ToArray(), Demand(Monday, 12, 7));

        var result = solver.Solve(input);

        AssertExactRoster(input, result);
        Assert.InRange(Hours(result, employees[0].Id), 3, 4);
        Assert.InRange(Hours(result, employees[1].Id), 3, 4);
    }

    [Fact]
    public void AdminCanDisableHistoricalCompensationWithoutDisablingCurrentFairness()
    {
        var employees = new[] { Driver(), Driver() };
        var dates = Enumerable.Range(0, 4).Select(Monday.AddDays).ToArray();
        var history = Enumerable.Range(1, 4).SelectMany(week => new[]
        {
            new RosterSolverHistory(employees[0].Id, Monday.AddDays(-7 * week), 28, 20),
            new RosterSolverHistory(employees[1].Id, Monday.AddDays(-7 * week), 12, 20)
        }).ToArray();
        var input = Input(employees,
            dates.SelectMany(date => employees.Select(employee => Available(employee, date, 12, 22))).ToArray(),
            dates.SelectMany(date => Demand(date, 12, 10)).ToArray(),
            options: new RosterSolverOptions { HistoryFairnessWeight = 0, MaxSolveSeconds = 2 }, history: history);

        var result = solver.Solve(input);

        AssertExactRoster(input, result);
        Assert.Equal(20, Hours(result, employees[0].Id));
        Assert.Equal(20, Hours(result, employees[1].Id));
    }

    [Fact]
    public void RejectsDuplicateHistoricalEmployeeWeeksInsteadOfDoubleCountingThem()
    {
        var employee = Driver();
        var duplicate = new RosterSolverHistory(employee.Id, Monday.AddDays(-7), 12, 20);
        var input = Input([employee], [Available(employee, Monday, 12, 18)], Demand(Monday, 12, 6),
            history: [duplicate, duplicate]);

        var result = solver.Solve(input);

        Assert.Equal("invalid", result.Status);
        Assert.NotEmpty(result.Diagnostics);
    }

    private static int Hours(RosterSolverResult result, Guid employeeId) =>
        result.Shifts.Where(shift => shift.EmployeeId == employeeId).Sum(shift => shift.DurationHours);
}
