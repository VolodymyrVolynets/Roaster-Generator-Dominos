using Roaster_Generator.Contracts.Roster;
using Roaster_Generator.Services;
using Roaster_Generator.Validation;

namespace Roaster_Generator.Tests;

public sealed partial class RosterSolverTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(11)]
    public void ManualValidationWarnsButAllowsShiftLengthsOutsideGenerationRange(int duration)
    {
        var employee = Driver();
        var input = Input([employee], [Available(employee, Monday, 10, 23)], Demand(Monday, 10, duration));

        var validation = RosterSolver.ValidateManualEdit(
            input,
            [new(employee.Id, Monday, 10, 10 + duration)]);

        Assert.Empty(validation.Errors);
        Assert.Contains(validation.Warnings, warning => warning.Contains("automatic generation", StringComparison.Ordinal));
        Assert.NotEmpty(RosterSolver.Validate(input, [new(employee.Id, Monday, 10, 10 + duration)]));
    }

    [Fact]
    public void ManualValidationAllowsClosedHoursButWarns()
    {
        var employee = Driver();
        var input = Input([employee], [Available(employee, Monday, 12, 18)], Demand(Monday, 12, 6));

        var validation = RosterSolver.ValidateManualEdit(input, [new(employee.Id, Monday, 10, 18)]);

        Assert.All(validation.Errors, error => Assert.StartsWith("Demand mismatch:", error));
        Assert.Contains(validation.Warnings, warning => warning.Contains("shop-hours override", StringComparison.Ordinal));
        Assert.Contains(validation.Warnings, warning => warning.Contains("shop opening or closing", StringComparison.Ordinal));
    }

    [Fact]
    public void ManualValidationStillRejectsUnavailableOpenHours()
    {
        var employee = Driver();
        var input = Input([employee], [Available(employee, Monday, 13, 18)], Demand(Monday, 12, 6));

        var validation = RosterSolver.ValidateManualEdit(input, [new(employee.Id, Monday, 12, 18)]);

        Assert.Contains(validation.Errors, error => error.Contains("outside their availability during configured shop hours", StringComparison.Ordinal));
    }

    [Fact]
    public void ManualValidationWarnsButAllowsStartsAfterGeneratorLimit()
    {
        var employee = Driver();
        var input = Input([employee], [Available(employee, Monday, 21, 0)], Demand(Monday, 21, 3));

        var validation = RosterSolver.ValidateManualEdit(input, [new(employee.Id, Monday, 21, 24)]);

        Assert.Empty(validation.Errors);
        Assert.Contains(validation.Warnings, warning => warning.Contains("start-time override", StringComparison.Ordinal));
    }
}

public sealed class RosterManualEditRequestValidatorTests
{
    private static readonly DateOnly Monday = new(2026, 9, 7);

    [Theory]
    [InlineData(12, 14)]
    [InlineData(12, 23)]
    [InlineData(24, 29)]
    public void AcceptsManualOverridesThatCanRoundTrip(int startHour, int finishHour)
    {
        var result = new RosterShiftUpdateRequestValidator().Validate(Shift(startHour, finishHour));

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(error => error.ErrorMessage)));
    }

    [Theory]
    [InlineData(12, 12)]
    [InlineData(12, 37)]
    [InlineData(5, 10)]
    [InlineData(30, 32)]
    public void RejectsStructurallyUnsafeManualShifts(int startHour, int finishHour)
    {
        Assert.False(new RosterShiftUpdateRequestValidator().Validate(Shift(startHour, finishHour)).IsValid);
    }

    private static RosterShiftUpdateRequest Shift(int startHour, int finishHour) => new()
    {
        EmployeeId = Guid.NewGuid(),
        Date = Monday,
        StartHour = startHour,
        FinishHour = finishHour
    };
}
