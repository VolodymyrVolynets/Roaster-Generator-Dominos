using System.Text.Json;
using Roaster_Generator.Contracts.Roster;

namespace Roaster_Generator.Tests;

public sealed class RosterMetricsTests
{
    [Fact]
    public void AverageWeightsEveryShiftEquallyRatherThanEveryEmployee()
    {
        var roster = new RosterPlanResponse
        {
            Employees = [Employee(10), Employee(3, 3, 3), Employee()]
        };

        Assert.Equal(4.75, roster.AverageHoursPerShift);
        Assert.Equal(10, roster.Employees[0].AverageHoursPerShift);
        Assert.Equal(3, roster.Employees[1].AverageHoursPerShift);
        Assert.Equal(0, roster.Employees[2].AverageHoursPerShift);
    }

    [Fact]
    public void EmptyRosterHasZeroAverageWithoutDivisionByZero()
    {
        Assert.Equal(0, new RosterPlanResponse().AverageHoursPerShift);
        Assert.Equal(0, new RosterPlanResponse { Employees = [Employee()] }.AverageHoursPerShift);
    }

    [Fact]
    public void AveragesAreRoundedToTwoDecimalPlaces()
    {
        var employee = Employee(3, 3, 4);
        Assert.Equal(3.33, employee.AverageHoursPerShift);
        Assert.Equal(3.33, new RosterPlanResponse { Employees = [employee] }.AverageHoursPerShift);
    }

    [Fact]
    public void OlderSnapshotComputesAverageFromItsOvernightShiftDurations()
    {
        const string oldSnapshot = """
            {"Employees":[{"Shifts":[
                {"StartTime":"22:00","FinishTime":"04:00","DurationHours":6},
                {"StartTime":"18:00","FinishTime":"02:00","DurationHours":8}
            ]}]}
            """;
        var roster = JsonSerializer.Deserialize<RosterPlanResponse>(oldSnapshot)!;
        Assert.Equal(7, roster.AverageHoursPerShift);
        Assert.Equal(7, roster.Employees[0].AverageHoursPerShift);
        using var saved = JsonDocument.Parse(JsonSerializer.Serialize(roster));
        Assert.Equal(7, saved.RootElement.GetProperty("AverageHoursPerShift").GetDouble());
        Assert.Equal(7, saved.RootElement.GetProperty("Employees")[0].GetProperty("AverageHoursPerShift").GetDouble());
    }

    private static RosterEmployeeResponse Employee(params int[] durations) => new()
    {
        Shifts = durations.Select(duration => new RosterShiftResponse { DurationHours = duration }).ToArray()
    };
}
