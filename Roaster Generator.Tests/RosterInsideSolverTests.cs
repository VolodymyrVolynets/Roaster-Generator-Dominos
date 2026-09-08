using Roaster_Generator.Entities;
using Roaster_Generator.Services;
using Roaster_Generator.Validation;

namespace Roaster_Generator.Tests;

public sealed partial class RosterSolverTests
{
    [Fact]
    public void RosterKindDefaultsToDriversAndRejectsUnknownKinds()
    {
        var input = Input([], [], []);

        Assert.Equal(RosterKinds.Drivers, input.RosterKind);
        Assert.Equal("invalid", solver.Solve(input with { RosterKind = "other" }).Status);
    }

    [Fact]
    public void AManagerCanCoverInsideDemandAlone()
    {
        var manager = InsideEmployee(manager: true);
        var input = InsideInput([manager], [Available(manager, Monday, 12, 18)], Demand(Monday, 12, 6));
        var progress = new List<RosterSolverProgress>();

        var result = solver.Solve(input, progress.Add);

        AssertExactInsideRoster(input, result);
        Assert.Equal(manager.Id, Assert.Single(result.Shifts).EmployeeId);
        Assert.Contains(progress, entry => entry.Message.Contains("inside employee-hour"));
        Assert.DoesNotContain(progress, entry => entry.Message.Contains("driver-hour"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void InsideEmployeesAlwaysNeedAManagerRegardlessOfHowManyAreAvailable(int employeeCount)
    {
        var employees = Enumerable.Range(0, employeeCount).Select(_ => InsideEmployee()).ToArray();
        var input = InsideInput(employees,
            employees.Select(employee => Available(employee, Monday, 12, 18)).ToArray(),
            Demand(Monday, 12, 6, requiredDrivers: employeeCount));

        var result = solver.Solve(input);

        AssertInfeasible(result);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("Monday 12:00") &&
            diagnostic.Contains("need at least one manager"));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Contains("driver", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void OneManagerCanCoverMultipleInsideEmployees(int insideEmployeeCount)
    {
        var employees = Enumerable.Range(0, insideEmployeeCount).Select(_ => InsideEmployee())
            .Append(InsideEmployee(manager: true)).ToArray();
        var input = InsideInput(employees,
            employees.Select(employee => Available(employee, Monday, 12, 18)).ToArray(),
            Demand(Monday, 12, 6, requiredDrivers: employees.Length));

        var result = solver.Solve(input);

        AssertExactInsideRoster(input, result);
        Assert.Equal(employees.Length, result.Shifts.Count);
    }

    [Fact]
    public void ManagersCanHandOverCoverageAtAnInsideShiftBoundary()
    {
        var instore = InsideEmployee();
        var first = InsideEmployee(manager: true);
        var second = InsideEmployee(manager: true);
        var input = InsideInput([instore, first, second],
            [Available(instore, Monday, 12, 18), Available(first, Monday, 12, 15), Available(second, Monday, 15, 18)],
            Demand(Monday, 12, 6, requiredDrivers: 2));

        var result = solver.Solve(input);

        AssertExactInsideRoster(input, result);
        Assert.Equal(15, Assert.Single(result.Shifts, shift => shift.EmployeeId == first.Id).FinishHour);
        Assert.Equal(15, Assert.Single(result.Shifts, shift => shift.EmployeeId == second.Id).StartHour);
    }

    [Fact]
    public void ManagerCoverageContinuesThroughEveryHourOfAnOvernightInsideShift()
    {
        var instore = InsideEmployee();
        var manager = InsideEmployee(manager: true);
        var input = InsideInput([instore, manager],
            [Available(instore, Monday, 20, 2), Available(manager, Monday, 20, 2)],
            Demand(Monday, 20, 6, requiredDrivers: 2));

        AssertExactInsideRoster(input, solver.Solve(input));

        var errors = RosterSolver.Validate(input,
            [new(instore.Id, Monday, 20, 26), new(manager.Id, Monday, 20, 24)]);
        Assert.Contains(errors, error => error.Contains("Monday 00:00 (+1 day)") &&
            error.Contains("need at least one manager"));
        Assert.DoesNotContain(errors, error => error.Contains("Monday 23:00"));
    }

    [Fact]
    public void GenerationRejectsAnInsideHourAfterTheOnlyManagerLeaves()
    {
        var instore = InsideEmployee();
        var manager = InsideEmployee(manager: true);
        var input = InsideInput([instore, manager],
            [Available(instore, Monday, 12, 18), Available(manager, Monday, 12, 15)],
            Demand(Monday, 12, 6, requiredDrivers: 2));

        var result = solver.Solve(input);

        AssertInfeasible(result);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("Monday 15:00") &&
            diagnostic.Contains("need at least one manager"));
    }

    [Fact]
    public void ADriverCannotSubstituteForAnInsideEmployeeOrManager()
    {
        var driver = Driver();
        var manager = InsideEmployee(manager: true);
        var input = InsideInput([driver, manager],
            [Available(driver, Monday, 12, 18), Available(manager, Monday, 12, 18)],
            Demand(Monday, 12, 6, requiredDrivers: 2));

        var result = solver.Solve(input);

        AssertInfeasible(result);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("need 1 more inside employee"));
        var errors = RosterSolver.Validate(input,
            [new(driver.Id, Monday, 12, 18), new(manager.Id, Monday, 12, 18)]);
        Assert.Contains(errors, error => error.Contains("ineligible inside employee") && error.Contains(driver.Id.ToString()));

        var noManager = input with { Employees = [driver] };
        Assert.Contains(solver.Solve(noManager).Diagnostics, diagnostic => diagnostic.Contains("need at least one manager"));
    }

    [Fact]
    public void RemovingEveryInsideShiftStillProducesABlockingManagerCoverageError()
    {
        var manager = InsideEmployee(manager: true);
        var input = InsideInput([manager], [Available(manager, Monday, 12, 18)], Demand(Monday, 12, 6));

        var errors = RosterSolver.Validate(input, []);

        Assert.Equal(6, errors.Count(error => error.StartsWith("Demand mismatch:")));
        Assert.Equal(6, errors.Count(error => error.Contains("need at least one manager")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActualInsideShiftsOutsidePositiveDemandStillRequireAManager(bool enteredZeroDemand)
    {
        var instore = InsideEmployee();
        var input = InsideInput([instore], [Available(instore, Monday, 12, 18)],
            enteredZeroDemand ? Demand(Monday, 12, 6, requiredDrivers: 0) : []);

        var errors = RosterSolver.Validate(input, [new(instore.Id, Monday, 12, 18)]);

        Assert.Contains(errors, error => error.StartsWith("Demand mismatch:"));
        Assert.Contains(errors, error => error.Contains("Monday 12:00") && error.Contains("need at least one manager"));
    }

    [Fact]
    public void InsideEditsWithAManagerCanHaveOnlyNonBlockingDemandWarnings()
    {
        var manager = InsideEmployee(manager: true);
        var input = InsideInput([manager], [Available(manager, Monday, 12, 18)], []);

        var errors = RosterSolver.Validate(input, [new(manager.Id, Monday, 12, 18)]);

        Assert.Equal(6, errors.Count);
        Assert.All(errors, error => Assert.StartsWith("Demand mismatch:", error));
    }

    [Fact]
    public void InsideFairnessUsesManagerTargetsBeforeAnyOtherProfiles()
    {
        var first = InsideEmployee(manager: true, targetHours: 10);
        first.InStoreProfile = new InStoreProfile { TargetHours = 100 };
        first.DriverProfile = new DriverProfile { TargetHours = 100 };
        var second = InsideEmployee(manager: true, targetHours: 20);
        var employees = new[] { first, second };
        var dates = Enumerable.Range(0, 3).Select(Monday.AddDays).ToArray();
        var input = InsideInput(employees,
            dates.SelectMany(date => employees.Select(employee => Available(employee, date, 12, 22))).ToArray(),
            dates.SelectMany(date => Demand(date, 12, 10)).ToArray());

        var result = solver.Solve(input);

        AssertExactInsideRoster(input, result);
        Assert.Equal(10, Hours(result, first.Id));
        Assert.Equal(20, Hours(result, second.Id));
        Assert.Equal(100, result.TargetUtilizationPercent);
    }

    [Fact]
    public void InsideFairnessUsesInStoreTargetsForEmployeesWhoAreNotManagers()
    {
        var manager = InsideEmployee(manager: true, targetHours: 20);
        var first = InsideEmployee(targetHours: 10);
        var second = InsideEmployee(targetHours: 10);
        var employees = new[] { manager, first, second };
        var dates = new[] { Monday, Monday.AddDays(1) };
        var input = InsideInput(employees,
            dates.SelectMany(date => employees.Select(employee => Available(employee, date, 12, 22))).ToArray(),
            dates.SelectMany(date => Demand(date, 12, 10, requiredDrivers: 2)).ToArray());

        var result = solver.Solve(input);

        AssertExactInsideRoster(input, result);
        Assert.Equal(20, Hours(result, manager.Id));
        Assert.Equal(10, Hours(result, first.Id));
        Assert.Equal(10, Hours(result, second.Id));
        Assert.Equal(100, result.TargetUtilizationPercent);
    }

    [Fact]
    public void InsideRosterCompensatesHistoricalHoursWhileKeepingCurrentFairness()
    {
        var employees = new[] { InsideEmployee(manager: true), InsideEmployee(manager: true) };
        var dates = Enumerable.Range(0, 4).Select(Monday.AddDays).ToArray();
        var history = Enumerable.Range(1, 4).SelectMany(week => new[]
        {
            new RosterSolverHistory(employees[0].Id, Monday.AddDays(-7 * week), 28, 20),
            new RosterSolverHistory(employees[1].Id, Monday.AddDays(-7 * week), 12, 20)
        }).ToArray();
        var input = InsideInput(employees,
            dates.SelectMany(date => employees.Select(employee => Available(employee, date, 12, 22))).ToArray(),
            dates.SelectMany(date => Demand(date, 12, 10)).ToArray(), history: history);

        var result = solver.Solve(input);

        AssertExactInsideRoster(input, result);
        var gap = Hours(result, employees[1].Id) - Hours(result, employees[0].Id);
        Assert.InRange(gap, 1, 6);
    }

    [Fact]
    public void InsideRosterCompensatesHistoricalShiftLengthWithoutChangingFairWeeklyHours()
    {
        var employees = new[] { InsideEmployee(manager: true, targetHours: 8), InsideEmployee(manager: true, targetHours: 8) };
        var input = ShiftLengthHistoryInput(employees,
        [
            new(employees[0].Id, Monday.AddDays(-7), 24, 24, ShiftCount: 6),
            new(employees[1].Id, Monday.AddDays(-7), 24, 24, ShiftCount: 3)
        ]) with { RosterKind = RosterKinds.Inside };

        var result = solver.Solve(input);

        AssertExactInsideRoster(input, result);
        Assert.All(employees, employee => Assert.Equal(8, Hours(result, employee.Id)));
        Assert.Equal(8, AverageLength(result, employees[0].Id));
        Assert.Equal(4, AverageLength(result, employees[1].Id));
    }

    [Fact]
    public void InsideRosterStillEnforcesRestFromNeighboringWeeks()
    {
        var manager = InsideEmployee(manager: true);
        var boundary = new RosterSolverBoundaryShift(manager.Id,
            Monday.AddDays(-1).ToDateTime(new TimeOnly(20, 0)), Monday.ToDateTime(new TimeOnly(6, 0)));
        var input = InsideInput([manager], [Available(manager, Monday, 12, 18)], Demand(Monday, 12, 6), [boundary]);

        AssertInfeasible(solver.Solve(input));
        Assert.Contains(RosterSolver.Validate(input, [new(manager.Id, Monday, 12, 18)]),
            error => error.Contains("less than 8 hours of rest"));
    }

    [Fact]
    public void InsideTargetValidationUsesTheSelectedRosterKind()
    {
        var employee = Driver(targetHours: 20);
        employee.InStoreProfile = new InStoreProfile { TargetHours = -1 };
        var input = Input([employee], [], []);

        Assert.Empty(RosterSolverInputValidator.ValidateInput(input));
        Assert.Contains(RosterSolverInputValidator.ValidateInput(input with { RosterKind = RosterKinds.Inside }),
            error => error.Contains("Employee target hours"));
    }

    private static Employee InsideEmployee(bool manager = false, int targetHours = 20) => new()
    {
        Id = Guid.NewGuid(), FirstName = "Test", LastName = manager ? "Manager" : "InStore",
        ManagerProfile = manager ? new ManagerProfile { TargetHours = targetHours } : null,
        InStoreProfile = manager ? null : new InStoreProfile { TargetHours = targetHours }
    };

    private static RosterSolverInput InsideInput(IReadOnlyList<Employee> employees, IReadOnlyList<Shift> availability,
        IReadOnlyList<RosterSolverDemand> demand, IReadOnlyList<RosterSolverBoundaryShift>? boundaries = null,
        RosterSolverOptions? options = null, IReadOnlyList<RosterSolverHistory>? history = null) =>
        Input(employees, availability, demand, boundaries, options, history) with { RosterKind = RosterKinds.Inside };

    private static void AssertExactInsideRoster(RosterSolverInput input, RosterSolverResult result)
    {
        AssertExactRoster(input, result);
        foreach (var slot in input.Demand.Where(slot => slot.RequiredDrivers > 0))
            Assert.Contains(result.Shifts, shift => shift.Date == slot.Date && shift.StartHour <= slot.Hour &&
                shift.FinishHour > slot.Hour && input.Employees.Single(employee => employee.Id == shift.EmployeeId).ManagerProfile is not null);
        Assert.All(result.Shifts, shift => Assert.Contains(input.Employees,
            employee => employee.Id == shift.EmployeeId && (employee.ManagerProfile is not null || employee.InStoreProfile is not null)));
    }
}
