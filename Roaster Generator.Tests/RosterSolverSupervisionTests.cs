using Roaster_Generator.Enums;
using Roaster_Generator.Services;

namespace Roaster_Generator.Tests;

public sealed partial class RosterSolverTests
{
    [Theory]
    [InlineData(DriverType.Car)]
    [InlineData(DriverType.Moped)]
    public void CarAndMopedDriversCanCoverDemandAlone(DriverType driverType)
    {
        var employee = Driver(driverType: driverType);
        var input = Input([employee], [Available(employee, Monday, 12, 18)], Demand(Monday, 12, 6));

        var result = solver.Solve(input);

        AssertExactRoster(input, result);
        Assert.Equal(employee.Id, Assert.Single(result.Shifts).EmployeeId);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AnyNumberOfEbikesCannotCoverDemandWithoutACarOrMoped(int ebikeCount)
    {
        var employees = Enumerable.Range(0, ebikeCount).Select(_ => Driver(driverType: DriverType.EBike)).ToArray();
        var input = Input(employees,
            employees.Select(employee => Available(employee, Monday, 12, 18)).ToArray(),
            Demand(Monday, 12, 6, requiredDrivers: ebikeCount));

        var result = solver.Solve(input);

        AssertInfeasible(result);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("Monday 12:00") &&
            diagnostic.Contains("car or moped", StringComparison.OrdinalIgnoreCase));
        var errors = RosterSolver.Validate(input,
            employees.Select(employee => new RosterSolverShift(employee.Id, Monday, 12, 18)).ToArray());
        Assert.Contains(errors, error => error.Contains("e-bike drivers are scheduled without a car or moped"));
    }

    [Theory]
    [InlineData(DriverType.Car, 1)]
    [InlineData(DriverType.Car, 2)]
    [InlineData(DriverType.Car, 3)]
    [InlineData(DriverType.Moped, 1)]
    [InlineData(DriverType.Moped, 2)]
    [InlineData(DriverType.Moped, 3)]
    public void OneCarOrMopedCanSupportMultipleEbikes(DriverType supportType, int ebikeCount)
    {
        var employees = Enumerable.Range(0, ebikeCount).Select(_ => Driver(driverType: DriverType.EBike))
            .Append(Driver(driverType: supportType)).ToArray();
        var input = Input(employees,
            employees.Select(employee => Available(employee, Monday, 12, 18)).ToArray(),
            Demand(Monday, 12, 6, requiredDrivers: employees.Length));

        var result = solver.Solve(input);

        AssertExactRoster(input, result);
        Assert.Equal(employees.Length, result.Shifts.Count);
    }

    [Fact]
    public void ACarAndMopedCanHandOverEbikeSupportAtAShiftBoundary()
    {
        var ebike = Driver(driverType: DriverType.EBike);
        var car = Driver();
        var moped = Driver(driverType: DriverType.Moped);
        var input = Input([ebike, car, moped],
            [Available(ebike, Monday, 12, 18), Available(car, Monday, 12, 15), Available(moped, Monday, 15, 18)],
            Demand(Monday, 12, 6, requiredDrivers: 2));

        var result = solver.Solve(input);

        AssertExactRoster(input, result);
        Assert.Equal(6, Assert.Single(result.Shifts, shift => shift.EmployeeId == ebike.Id).DurationHours);
        Assert.Equal(15, Assert.Single(result.Shifts, shift => shift.EmployeeId == car.Id).FinishHour);
        Assert.Equal(15, Assert.Single(result.Shifts, shift => shift.EmployeeId == moped.Id).StartHour);
    }

    [Fact]
    public void EbikeSupportMustCoverEveryHourOfAnOvernightShift()
    {
        var ebike = Driver(driverType: DriverType.EBike);
        var moped = Driver(driverType: DriverType.Moped);
        var input = Input([ebike, moped],
            [Available(ebike, Monday, 20, 2), Available(moped, Monday, 20, 2)],
            Demand(Monday, 20, 6, requiredDrivers: 2));

        AssertExactRoster(input, solver.Solve(input));

        var errors = RosterSolver.Validate(input,
            [new(ebike.Id, Monday, 20, 26), new(moped.Id, Monday, 20, 24)]);
        Assert.Contains(errors, error => error.Contains("Monday 00:00 (+1 day)") &&
            error.Contains("e-bike drivers are scheduled without a car or moped"));
        Assert.DoesNotContain(errors, error => error.Contains("Monday 23:00"));
    }

    [Fact]
    public void GenerationRejectsAnEbikeWhoseSupportFinishesTooEarly()
    {
        var ebike = Driver(driverType: DriverType.EBike);
        var moped = Driver(driverType: DriverType.Moped);
        var input = Input([ebike, moped],
            [Available(ebike, Monday, 12, 18), Available(moped, Monday, 12, 15)],
            Demand(Monday, 12, 6, requiredDrivers: 2));

        var result = solver.Solve(input);

        AssertInfeasible(result);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("Monday 15:00") &&
            diagnostic.Contains("car or moped", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ManualValidationRejectsUnsupportedEbikesEvenWithoutPositiveDemand(bool enteredZeroDemand)
    {
        var ebike = Driver(driverType: DriverType.EBike);
        var input = Input([ebike], [Available(ebike, Monday, 12, 18)],
            enteredZeroDemand ? Demand(Monday, 12, 6, requiredDrivers: 0) : []);

        var errors = RosterSolver.Validate(input, [new(ebike.Id, Monday, 12, 18)]);

        Assert.Contains(errors, error => error.StartsWith("Demand mismatch:"));
        Assert.Contains(errors, error => error.Contains("Monday 12:00") &&
            error.Contains("e-bike drivers are scheduled without a car or moped"));
    }

    [Theory]
    [InlineData(DriverType.Car)]
    [InlineData(DriverType.Moped)]
    public void ManualValidationAllowsSupportedEbikesWithOnlyDemandWarnings(DriverType supportType)
    {
        var employees = new[] { Driver(driverType: DriverType.EBike), Driver(driverType: supportType) };
        var input = Input(employees,
            employees.Select(employee => Available(employee, Monday, 12, 18)).ToArray(), []);

        var errors = RosterSolver.Validate(input,
            employees.Select(employee => new RosterSolverShift(employee.Id, Monday, 12, 18)).ToArray());

        Assert.NotEmpty(errors);
        Assert.All(errors, error => Assert.StartsWith("Demand mismatch:", error));
    }

    [Fact]
    public void AnUncoveredHourProducesOnlyADemandWarning()
    {
        var employee = Driver();
        var input = Input([employee], [Available(employee, Monday, 12, 18)], Demand(Monday, 12, 6));

        var errors = RosterSolver.Validate(input, []);

        Assert.Equal(6, errors.Count);
        Assert.All(errors, error => Assert.StartsWith("Demand mismatch:", error));
    }
}
