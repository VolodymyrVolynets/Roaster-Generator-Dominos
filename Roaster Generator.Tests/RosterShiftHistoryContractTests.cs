using System.Text.Json;
using Roaster_Generator.Contracts.Roster;
using Roaster_Generator.Entities;
using Roaster_Generator.Services;
using Roaster_Generator.Validation;

namespace Roaster_Generator.Tests;

public sealed class RosterShiftHistoryContractTests
{
    private static readonly DateOnly Monday = new(2026, 9, 7);

    [Fact]
    public void HistoricalShiftCountUsesSavedDurationsIncludingOvernightShifts()
    {
        const string snapshot = """
            {
                "ScheduledHours": 14,
                "Shifts": [
                    {"StartTime":"20:00","FinishTime":"02:00","FinishDayOffset":1,"DurationHours":6},
                    {"StartTime":"18:00","FinishTime":"02:00","FinishDayOffset":1,"DurationHours":8}
                ]
            }
            """;
        var employee = JsonSerializer.Deserialize<RosterEmployeeResponse>(snapshot)!;

        Assert.Equal(2, RosterInputService.ReadHistoryShiftCount(employee));
        Assert.Equal(7, employee.AverageHoursPerShift);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(12, null)]
    public void EmptyShiftListDistinguishesZeroWorkFromUnavailableHistory(int scheduledHours, int? expected)
    {
        var employee = new RosterEmployeeResponse { ScheduledHours = scheduledHours };

        Assert.Equal(expected, RosterInputService.ReadHistoryShiftCount(employee));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    public void ExplicitlyMissingShiftDataStaysUnknownEvenWhenScheduledHoursAreZero(int scheduledHours)
    {
        var employee = new RosterEmployeeResponse { ScheduledHours = scheduledHours, Shifts = null! };

        Assert.Null(RosterInputService.ReadHistoryShiftCount(employee));
    }

    [Theory]
    [InlineData(12, 6, 0)]
    [InlineData(5, 6, -1)]
    [InlineData(12, 6, 5)]
    [InlineData(0, 6, 6)]
    public void InvalidOrInconsistentShiftDurationsAreExcludedFromShiftHistory(
        int scheduledHours, int firstDuration, int secondDuration)
    {
        var employee = Employee(scheduledHours, firstDuration, secondDuration);

        Assert.Null(RosterInputService.ReadHistoryShiftCount(employee));
    }

    [Fact]
    public void CorruptLargeDurationsAreRejectedWithoutIntegerOverflow()
    {
        var employee = Employee(-2, int.MaxValue, int.MaxValue);

        Assert.Null(RosterInputService.ReadHistoryShiftCount(employee));
    }

    [Fact]
    public void OlderSnapshotsWithoutShiftDurationsDoNotInventAHistoryAverage()
    {
        const string snapshot = """
            {"ScheduledHours":12,"TargetHours":20,"Shifts":[{"StartTime":"12:00","FinishTime":"18:00"}]}
            """;
        var employee = JsonSerializer.Deserialize<RosterEmployeeResponse>(snapshot)!;

        Assert.Null(RosterInputService.ReadHistoryShiftCount(employee));
        Assert.Null(employee.PreviousShiftCount);
        Assert.Null(employee.PreviousAverageHoursPerShift);
        Assert.Equal(12, employee.ScheduledHours);
        Assert.Equal(20, employee.TargetHours);
    }

    [Fact]
    public void HistoryRecordWithoutOptionalShiftCountRemainsCompatible()
    {
        const string snapshot = """
            {"EmployeeId":"00000000-0000-0000-0000-000000000001","WeekStart":"2026-08-31","ScheduledHours":12,"TargetHours":20}
            """;
        var historicalWeek = JsonSerializer.Deserialize<RosterSolverHistory>(snapshot)!;

        Assert.Null(historicalWeek.ShiftCount);
        Assert.Equal(12, historicalWeek.ScheduledHours);
        Assert.Equal(20, historicalWeek.TargetHours);
        Assert.Null(new RosterSolverHistory(Guid.NewGuid(), Monday.AddDays(-7), 12, 20).ShiftCount);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0, null)]
    [InlineData(8, 6.25)]
    public void SavedHistoricalShiftMetricsSurviveSnapshotSerialization(int? shiftCount, double? average)
    {
        var employee = new RosterEmployeeResponse
        {
            PreviousShiftCount = shiftCount,
            PreviousAverageHoursPerShift = average
        };

        var restored = JsonSerializer.Deserialize<RosterEmployeeResponse>(JsonSerializer.Serialize(employee))!;

        Assert.Equal(shiftCount, restored.PreviousShiftCount);
        Assert.Equal(average, restored.PreviousAverageHoursPerShift);
    }

    [Fact]
    public void NewHistoryShiftLengthPreferenceDefaultsTo100IncludingOlderRequestJson()
    {
        Assert.Equal(100, new RosterSettingsRequest().HistoryShiftLengthWeight);
        Assert.Equal(100, new RosterGenerationSettings().HistoryShiftLengthWeight);
        Assert.Equal(100, new RosterSolverOptions().HistoryShiftLengthWeight);
        Assert.Equal(100, JsonSerializer.Deserialize<RosterSettingsRequest>("{}")!.HistoryShiftLengthWeight);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(1000)]
    public void SavedPreferenceMapsToApiAndSolverWithoutReplacingAnExplicitZero(int weight)
    {
        var settings = new RosterGenerationSettings { HistoryShiftLengthWeight = weight };

        var response = RosterSettingsService.ToResponse(settings);
        var options = RosterSettingsService.ToOptions(response);

        Assert.Equal(weight, response.HistoryShiftLengthWeight);
        Assert.Equal(weight, options.HistoryShiftLengthWeight);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1000, true)]
    [InlineData(1001, false)]
    public void AdminPreferenceUsesTheSameAllowedRangeAsOtherWeights(int weight, bool expectedValid)
    {
        var request = new RosterSettingsRequest { HistoryShiftLengthWeight = weight };

        var validation = new RosterSettingsRequestValidator().Validate(request);

        Assert.Equal(expectedValid, validation.IsValid);
        if (!expectedValid)
            Assert.Contains(validation.Errors, error => error.PropertyName == nameof(request.HistoryShiftLengthWeight));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(10000, true)]
    [InlineData(10001, false)]
    public void DirectSolverInputValidatesTheHistoryShiftLengthWeight(int weight, bool expectedValid)
    {
        var input = new RosterSolverInput(Monday, [], [], [], [],
            new RosterSolverOptions { HistoryShiftLengthWeight = weight });

        var errors = RosterSolverInputValidator.ValidateInput(input);

        Assert.Equal(expectedValid, errors.Count == 0);
        if (!expectedValid)
            Assert.Contains(errors, error => error.Contains("weights", StringComparison.OrdinalIgnoreCase));
    }

    private static RosterEmployeeResponse Employee(int scheduledHours, params int[] durations) => new()
    {
        ScheduledHours = scheduledHours,
        Shifts = durations.Select(duration => new RosterShiftResponse { DurationHours = duration }).ToArray()
    };
}
