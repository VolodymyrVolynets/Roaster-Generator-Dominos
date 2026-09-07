using Roaster_Generator.Services;

namespace Roaster_Generator.Tests;

public sealed partial class RosterSolverTests
{
    [Fact]
    public void LatestStartAt22AllowsAnOvernightFinishAt01()
    {
        var employee = Driver();
        var input = Input([employee], [Available(employee, Monday, 22, 1)], Demand(Monday, 22, 3));

        var result = solver.Solve(input);

        AssertExactRoster(input, result);
        var shift = Assert.Single(result.Shifts);
        Assert.Equal(22, shift.StartHour);
        Assert.Equal(25, shift.FinishHour);
    }

    [Theory]
    [InlineData(23, 2, 23)]
    [InlineData(0, 3, 24)]
    [InlineData(1, 4, 25)]
    public void RefusesLateStartsEvenWhenPreferenceWeightsAndMinimumRestAreZero(int start, int finish, int businessHour)
    {
        var employee = Driver();
        var input = Input([employee], [Available(employee, Monday, start, finish)], Demand(Monday, businessHour, 3),
            options: new RosterSolverOptions
            {
                TargetHoursWeight = 0, HistoryFairnessWeight = 0, FairnessSpreadWeight = 0,
                LongShiftBonus = 0, ShortShiftPenalty = 0, DailyShiftCountPenalty = 0,
                ShortBreakPenalty = 0, MinimumRestHours = 0, PreferredRestHours = 0, MaxSolveSeconds = 1
            });

        var result = solver.Solve(input);

        AssertInfeasible(result);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("22:00", StringComparison.Ordinal) &&
            diagnostic.Contains($"{start:00}:00", StringComparison.Ordinal) && diagnostic.Contains("need 1", StringComparison.Ordinal));
    }

    [Fact]
    public void EarlierLongShiftStillCoversDemandAfterMidnight()
    {
        var employee = Driver();
        var input = Input([employee], [Available(employee, Monday, 18, 4)], Demand(Monday, 18, 10));

        var result = solver.Solve(input);

        AssertExactRoster(input, result);
        var shift = Assert.Single(result.Shifts);
        Assert.Equal(18, shift.StartHour);
        Assert.Equal(28, shift.FinishHour);
        Assert.Equal(10, shift.DurationHours);
    }

    [Theory]
    [InlineData(23, 2, 23)]
    [InlineData(0, 3, 24)]
    [InlineData(1, 4, 25)]
    public void IndependentValidationRejectsLateStartsEvenWithExactCoverage(int start, int finish, int businessHour)
    {
        var employee = Driver();
        var input = Input([employee], [Available(employee, Monday, start, finish)], Demand(Monday, businessHour, 3));
        var shifts = new[] { new RosterSolverShift(employee.Id, Monday, businessHour, businessHour + 3) };

        var errors = RosterSolver.Validate(input, shifts);

        Assert.Contains(errors, error => error.Contains("must start by 22:00", StringComparison.Ordinal));
        Assert.DoesNotContain(errors, error => error.Contains("demand", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SolverProgressReportsTheHardLatestStartRule()
    {
        var employee = Driver();
        var input = Input([employee], [Available(employee, Monday, 22, 1)], Demand(Monday, 22, 3));
        var messages = new List<RosterSolverProgress>();

        AssertExactRoster(input, solver.Solve(input, messages.Add));

        Assert.Contains(messages, message => message.Message.Contains("hard 22:00 latest shift start", StringComparison.Ordinal));
    }
}
