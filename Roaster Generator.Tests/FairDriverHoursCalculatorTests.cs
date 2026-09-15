using Roaster_Generator.Entities;
using Roaster_Generator.Enums;
using Roaster_Generator.Services;

namespace Roaster_Generator.Tests;

public sealed class FairDriverHoursCalculatorTests
{
    private static readonly DateOnly Monday = new(2026, 9, 7);
    private readonly FairDriverHoursCalculator calculator = new();

    [Fact]
    public void IdenticalAvailabilityReceivesEqualExpectedHours()
    {
        var first = Driver();
        var second = Driver();
        var result = Calculate([first, second],
            [Available(first, 10, 20), Available(second, 10, 20)], Demand(10, 10));

        Assert.Equal(5, result.Drivers[first.Id].ExpectedHours, 4);
        Assert.Equal(result.Drivers[first.Id].ExpectedHours, result.Drivers[second.Id].ExpectedHours);
    }

    [Fact]
    public void MoreUsefulAvailabilityReceivesMoreExpectedHours()
    {
        var broad = Driver();
        var limited = Driver();
        var result = Calculate([broad, limited],
            [Available(broad, 10, 20), Available(limited, 10, 15)], Demand(10, 10));

        Assert.True(result.Drivers[broad.Id].ExpectedHours > result.Drivers[limited.Id].ExpectedHours);
    }

    [Fact]
    public void FairnessCompressionIncreasesLimitedDriversShare()
    {
        var broad = Driver();
        var limited = Driver();
        var availability = new[] { Available(broad, 10, 20), Available(limited, 10, 12) };
        var demand = Demand(10, 10);

        var proportional = calculator.Calculate([broad, limited], availability, demand, alpha: 1);
        var compressed = calculator.Calculate([broad, limited], availability, demand, alpha: 0.4);

        Assert.True(compressed.Drivers[limited.Id].ExpectedHours > proportional.Drivers[limited.Id].ExpectedHours);
    }

    [Fact]
    public void ScarceHighDemandAvailabilityHasMoreValue()
    {
        var scarce = Driver();
        var busy = Driver();
        var competitors = Enumerable.Range(0, 3).Select(_ => Driver()).ToArray();
        var drivers = new[] { scarce, busy }.Concat(competitors).ToArray();
        var availability = new[] { Available(scarce, 10, 11), Available(busy, 11, 12) }
            .Concat(competitors.Select(driver => Available(driver, 11, 12))).ToArray();
        var demand = new[]
        {
            new RosterSolverDemand(Monday, 10, 2),
            new RosterSolverDemand(Monday, 11, 1)
        };

        var result = Calculate(drivers, availability, demand);

        Assert.True(result.Drivers[scarce.Id].RawScore > result.Drivers[busy.Id].RawScore);
    }

    [Fact]
    public void ZeroDemandAvailabilityAddsNoScoreOrCapacity()
    {
        var driver = Driver();
        var result = Calculate([driver], [Available(driver, 10, 20)],
            Enumerable.Range(10, 10).Select(hour => new RosterSolverDemand(Monday, hour, 0)).ToArray());

        Assert.Equal(0, result.Drivers[driver.Id].RawScore);
        Assert.Equal(0, result.Drivers[driver.Id].CapacityHours);
        Assert.Equal(0, result.Drivers[driver.Id].ExpectedHours);
    }

    [Fact]
    public void DemandWithNoAvailableDriverRemainsUnallocated()
    {
        var driver = Driver();
        var result = Calculate([driver], [], Demand(10, 3));

        Assert.Equal(0, result.TotalAllocatedHours);
        Assert.Equal(3, result.UnallocatedDemandHours);
    }

    [Fact]
    public void ExpectedHoursAreCappedByUsefulCapacityAndRedistributed()
    {
        var limited = Driver();
        var broad = Driver();
        var result = Calculate([limited, broad],
            [Available(limited, 10, 12), Available(broad, 10, 20)], Demand(10, 10), alpha: 0);

        Assert.Equal(2, result.Drivers[limited.Id].ExpectedHours, 4);
        Assert.Equal(8, result.Drivers[broad.Id].ExpectedHours, 4);
        Assert.Equal(10, result.TotalAllocatedHours, 4);
    }

    [Fact]
    public void SufficientCapacityAllocatesAllDemandHours()
    {
        var drivers = Enumerable.Range(0, 3).Select(_ => Driver()).ToArray();
        var result = Calculate(drivers, drivers.Select(driver => Available(driver, 10, 20)).ToArray(),
            Demand(10, 10, required: 2));

        Assert.Equal(20, result.TotalAllocatedHours, 4);
        Assert.Equal(0, result.UnallocatedDemandHours, 4);
    }

    [Fact]
    public void CapacityShortageLeavesDemandUnallocated()
    {
        var first = Driver();
        var second = Driver();
        var result = Calculate([first, second],
            [Available(first, 10, 13), Available(second, 10, 13)], Demand(10, 6, required: 2));

        Assert.Equal(6, result.TotalAllocatedHours, 4);
        Assert.Equal(6, result.UnallocatedDemandHours, 4);
    }

    [Fact]
    public void ApproximateHoursAreCappedAtFortyFivePerDriverPerWeek()
    {
        var driver = Driver();
        var availability = Enumerable.Range(0, 7)
            .Select(day => Available(driver, 10, 20, Monday.AddDays(day)))
            .ToArray();
        var demand = Enumerable.Range(0, 7)
            .SelectMany(day => Enumerable.Range(10, 10)
                .Select(hour => new RosterSolverDemand(Monday.AddDays(day), hour, 1)))
            .ToArray();

        var result = Calculate([driver], availability, demand);

        Assert.Equal(45, result.Drivers[driver.Id].CapacityHours, 4);
        Assert.Equal(45, result.Drivers[driver.Id].ExpectedHours, 4);
        Assert.Equal(25, result.UnallocatedDemandHours, 4);
    }

    [Fact]
    public void OvernightAvailabilityUsesBusinessDayHours()
    {
        var driver = Driver();
        var result = Calculate([driver], [Available(driver, 20, 2)],
            Enumerable.Range(20, 6).Select(hour => new RosterSolverDemand(Monday, hour, 1)).ToArray());

        Assert.Equal(6, result.Drivers[driver.Id].CapacityHours, 4);
        Assert.Equal(6, result.Drivers[driver.Id].ExpectedHours, 4);
    }

    [Fact]
    public void SolverKeepsCoverageAboveApproximateHoursPreference()
    {
        var available = Driver();
        var preferredButUnavailable = Driver();
        var demand = Demand(10, 3);
        var expected = new Dictionary<Guid, FairDriverHoursAllocation>
        {
            [available.Id] = new(available.Id, 0, 0, 0, 0),
            [preferredButUnavailable.Id] = new(preferredButUnavailable.Id, 3, 3, 1, 1)
        };
        var input = new RosterSolverInput(Monday, [available, preferredButUnavailable],
            [Available(available, 10, 13)], demand, [],
            new RosterSolverOptions { ApproximateHoursWeight = 1000, MaxSolveSeconds = 2 },
            ExpectedHoursByEmployee: expected);

        var result = new RosterSolver().Solve(input);

        Assert.True(result.Success, result.Message);
        Assert.Equal(available.Id, Assert.Single(result.Shifts).EmployeeId);
        Assert.Equal(3, result.TotalScheduledHours);
    }

    private FairDriverHoursResult Calculate(IReadOnlyList<Employee> drivers,
        IReadOnlyList<Shift> availability, IReadOnlyList<RosterSolverDemand> demand,
        double alpha = 0.7) => calculator.Calculate(drivers, availability, demand, alpha);

    private static Employee Driver() => new()
    {
        Id = Guid.NewGuid(),
        IsActive = true,
        DriverProfile = new DriverProfile { DriverType = DriverType.Car }
    };

    private static Shift Available(Employee employee, int start, int finish, DateOnly? date = null) => new()
    {
        EmployeeId = employee.Id,
        Date = date ?? Monday,
        StartTime = new TimeOnly(start, 0),
        FinishTime = new TimeOnly(finish, 0)
    };

    private static RosterSolverDemand[] Demand(int start, int duration, int required = 1) =>
        Enumerable.Range(start, duration)
            .Select(hour => new RosterSolverDemand(Monday, hour, required)).ToArray();
}
