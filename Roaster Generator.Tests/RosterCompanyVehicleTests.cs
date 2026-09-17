using Roaster_Generator.Enums;
using Roaster_Generator.Services;

namespace Roaster_Generator.Tests;

public sealed partial class RosterSolverTests
{
    [Theory]
    [InlineData(DriverType.Car)]
    [InlineData(DriverType.Moped)]
    [InlineData(DriverType.EBike)]
    public void SharedVehicleLimitsApplyToEveryGeneratedHourAndManualEdits(DriverType type)
    {
        var first = Driver(type);
        var second = Driver(type);
        first.DriverProfile!.IsOwn = second.DriverProfile!.IsOwn = false;
        var support = Driver();
        var employees = new[] { first, second, support };
        var input = Input(employees, employees.Select(e => Available(e, Monday, 12, 18)).ToArray(),
            Demand(Monday, 12, 6, 3), options: new RosterSolverOptions
            { CompanyCars = 1, CompanyMopeds = 1, CompanyEBikes = 1, MaxSolveSeconds = 2 });

        var result = solver.Solve(input);

        AssertPartialRoster(input, result);
        Assert.Equal(12, result.TotalScheduledHours);
        foreach (var hour in Enumerable.Range(12, 6))
            Assert.InRange(result.Shifts.Count(s => s.EmployeeId != support.Id && s.StartHour <= hour && s.FinishHour > hour), 0, 1);
        var manual = RosterSolver.ValidateManualEdit(input, employees.Select(e => new RosterSolverShift(e.Id, Monday, 12, 18)).ToArray());
        Assert.Contains(manual.Errors, error => error.Contains($"company {type} capacity exceeded"));
    }

    [Theory]
    [InlineData(DriverType.Car)]
    [InlineData(DriverType.Moped)]
    [InlineData(DriverType.EBike)]
    public void OwnVehiclesDoNotConsumeCompanyCapacity(DriverType type)
    {
        var employees = new[] { Driver(type), Driver(type), Driver() };
        var input = Input(employees, employees.Select(e => Available(e, Monday, 12, 18)).ToArray(), Demand(Monday, 12, 6, 3));
        AssertExactRoster(input, solver.Solve(input));
    }

    [Theory]
    [InlineData(DriverType.Car)]
    [InlineData(DriverType.Moped)]
    [InlineData(DriverType.EBike)]
    public void ZeroCompanyVehiclesExcludesOnlyCompanyDrivers(DriverType type)
    {
        var company = Driver(type);
        company.DriverProfile!.IsOwn = false;
        var own = Driver();
        var employees = new[] { company, own };
        var input = Input(employees, employees.Select(e => Available(e, Monday, 12, 18)).ToArray(), Demand(Monday, 12, 6, 2));
        var result = solver.Solve(input);
        AssertPartialRoster(input, result);
        Assert.Equal(own.Id, Assert.Single(result.Shifts).EmployeeId);
    }

    [Fact]
    public void CompanyVehicleCanBeHandedOverAtTheEndOfAShift()
    {
        var first = Driver();
        var second = Driver();
        first.DriverProfile!.IsOwn = second.DriverProfile!.IsOwn = false;
        var input = Input([first, second], [Available(first, Monday, 12, 15), Available(second, Monday, 15, 18)],
            Demand(Monday, 12, 6), options: new RosterSolverOptions { CompanyCars = 1, MaxSolveSeconds = 2 });
        AssertExactRoster(input, solver.Solve(input));
    }

    [Fact]
    public void FleetCapacityRemainsHardDuringPreferenceOptimizationAndOvernightShifts()
    {
        var company = Driver();
        var otherCompany = Driver();
        company.DriverProfile!.IsOwn = otherCompany.DriverProfile!.IsOwn = false;
        var own = Driver();
        var employees = new[] { company, otherCompany, own };
        var input = Input(employees, employees.Select(e => Available(e, Monday, 20, 2)).ToArray(), Demand(Monday, 20, 6, 2),
            options: new RosterSolverOptions { CompanyCars = 1, ApproximateHoursWeight = 1000, MaxSolveSeconds = 3 });
        AssertExactRoster(input, solver.Solve(input));
        var edited = RosterSolver.ValidateManualEdit(input,
            [new(company.Id, Monday, 20, 26), new(otherCompany.Id, Monday, 20, 26)]);
        Assert.Contains(edited.Errors, error => error.Contains("00:00 (+1 day)") && error.Contains("company Car"));
    }

    [Fact]
    public void NeighboringWeekCanOccupyACompanyVehicleEvenForAnUnlistedDriver()
    {
        var company = Driver();
        company.DriverProfile!.IsOwn = false;
        var input = Input([company], [Available(company, Monday, 12, 18)], Demand(Monday, 12, 6),
            boundaries: [new(Guid.NewGuid(), Monday.ToDateTime(new TimeOnly(10, 0)), Monday.ToDateTime(new TimeOnly(15, 0)), DriverType.Car)],
            options: new RosterSolverOptions { CompanyCars = 1, MaxSolveSeconds = 2 });
        var result = solver.Solve(input);
        AssertPartialRoster(input, result);
        Assert.Equal(15, Assert.Single(result.Shifts).StartHour);
        Assert.Contains(RosterSolver.ValidateManualEdit(input, [new(company.Id, Monday, 12, 18)]).Errors,
            error => error.Contains("company Car capacity exceeded"));
    }

    [Fact]
    public void FleetCountsDoNotAffectInsideRosters()
    {
        var manager = Driver();
        manager.ManagerProfile = new();
        manager.DriverProfile!.IsOwn = false;
        var input = Input([manager], [Available(manager, Monday, 12, 18)], Demand(Monday, 12, 6))
            with { RosterKind = RosterKinds.Inside };
        AssertExactRoster(input, solver.Solve(input));
    }
}
