using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Roaster_Generator.Contracts.Roster;
using Roaster_Generator.Controllers;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Security;
using Roaster_Generator.Services;
using Roaster_Generator.Validation;

namespace Roaster_Generator.Tests;

public sealed class RosterLabourTests
{
    private static readonly DateOnly Monday = new(2026, 9, 14);

    [Fact]
    public async Task CostsOnlySavedDriverRosterAndIgnoresExistingInsideRosters()
    {
        using var db = NewDb();
        var drivers = Plan(db, RosterKinds.Drivers);
        var inside = Plan(db, RosterKinds.Inside);
        var firstDriver = Employee(db, 14.5m);
        var secondDriver = Employee(db, 18m);
        var instore = Employee(db, 15m);
        var manager = Employee(db, 20m, manager: true);
        foreach (var day in new[] { Monday, Monday.AddDays(6) })
        {
            Shift(drivers, firstDriver, day, 12, 18);
            Shift(drivers, secondDriver, day, 12, 16);
            Shift(inside, instore, day, 12, 20);
            Shift(inside, manager, day, 12, 20);
        }
        Demand(db, 1000m);
        await db.SaveChangesAsync();

        var result = await new RosterLabourService(db).GetAsync(Monday, default);

        Assert.True(result.Drivers.IsComplete);
        Assert.Equal(159m, result.Days[0].Drivers.LabourCost);
        Assert.Equal(198.75m, result.Days[6].Drivers.LabourCost);
        Assert.Equal(357.75m, result.Drivers.LabourCost);
        Assert.Equal(20m, result.Drivers.ScheduledHours);
        Assert.Equal(7000m, result.TargetSales);
        Assert.Equal(5.11m, result.Drivers.LabourPercentage);
        Assert.Equal(19.88m, result.Days[6].Drivers.LabourPercentage);
        Assert.Empty(result.Warnings);
        Assert.Equal(2, await db.RosterPlans.CountAsync());
    }

    [Theory]
    [InlineData(5, 20, 2, "94.25")]
    [InlineData(6, 20, 2, "101.50")]
    [InlineData(5, 0, 3, "54.38")]
    [InlineData(6, 0, 3, "43.50")]
    [InlineData(0, 20, 2, "87.00")]
    public async Task PremiumFollowsCalendarSundayWhileCostsStayOnRosterBusinessDay(
        int businessDay, int start, int finish, string expectedCost)
    {
        using var db = NewDb();
        var driverPlan = Plan(db, RosterKinds.Drivers);
        Shift(driverPlan, Employee(db, 14.5m), Monday.AddDays(businessDay), start, finish);
        Demand(db, 1000m);
        await db.SaveChangesAsync();

        var result = await new RosterLabourService(db).GetAsync(Monday, default);

        var cost = decimal.Parse(expectedCost, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(cost, result.Drivers.LabourCost);
        Assert.Equal(cost, result.Days[businessDay].Drivers.LabourCost);
        Assert.All(result.Days.Where((_, index) => index != businessDay),
            day => Assert.Equal(0m, day.Drivers.LabourCost));
    }

    [Fact]
    public async Task SundayRateKeepsFractionalCentsUntilDailyGroupTotalThenAggregatesConsistently()
    {
        using var db = NewDb();
        var drivers = Plan(db, RosterKinds.Drivers);
        var inside = Plan(db, RosterKinds.Inside);
        var sunday = Monday.AddDays(6);
        Shift(drivers, Employee(db, 14.5m), sunday, 12, 15);
        Shift(drivers, Employee(db, 14.5m), sunday, 12, 15);
        Shift(inside, Employee(db, 14.5m), sunday, 12, 15);
        Demand(db, 1000m);
        await db.SaveChangesAsync();

        var result = await new RosterLabourService(db).GetAsync(Monday, default);

        // 18.125 * 6 = 108.75; rounding the rate first would incorrectly yield 108.78.
        Assert.Equal(108.75m, result.Drivers.LabourCost);
        Assert.Equal(result.Drivers.LabourCost, result.Days.Sum(day => day.Drivers.LabourCost));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public async Task LegacyDriverShiftsKeepCurrentManagerPayExemption(
        bool managerProfile, bool managerRole, bool expectedExemption)
    {
        using var db = NewDb();
        var drivers = Plan(db, RosterKinds.Drivers);
        var employee = Employee(db, 20m, managerProfile);
        AddUser(db, employee, managerRole ? RoleNames.Manager : RoleNames.InStore);
        Shift(drivers, employee, Monday.AddDays(6), 12, 16);
        await db.SaveChangesAsync();

        var result = await new RosterLabourService(db).GetAsync(Monday, default);

        Assert.Equal(expectedExemption ? 80m : 100m, result.Drivers.LabourCost);
    }

    [Fact]
    public async Task UsesEditedShiftsCurrentRatesAndCurrentSalesInsteadOfSnapshotOrOtherWeeks()
    {
        using var db = NewDb();
        var current = Plan(db, RosterKinds.Drivers);
        current.SnapshotJson = "{\"TotalScheduledHours\":9999}";
        var past = Plan(db, RosterKinds.Drivers, Monday.AddDays(-7));
        var future = Plan(db, RosterKinds.Inside, Monday.AddDays(7));
        var employee = Employee(db, 14.5m);
        var shift = Shift(current, employee, Monday, 12, 15);
        Shift(past, employee, Monday.AddDays(-7), 12, 22);
        Shift(future, employee, Monday.AddDays(7), 12, 22);
        var oldDemand = Demand(db, 9999m);
        oldDemand.UpdatedAtUtc = DateTimeOffset.UtcNow.AddDays(-1);
        var currentDemand = Demand(db, 1000m);
        currentDemand.WeekStart = Monday.AddDays(-28);
        await db.SaveChangesAsync();
        var service = new RosterLabourService(db);

        var original = await service.GetAsync(Monday, default);
        Assert.Equal(43.5m, original.Drivers.LabourCost);
        Assert.Equal(currentDemand.Id, original.DemandPlanId);
        Assert.Equal(7000m, original.TargetSales);

        employee.HourlyRate = 20m;
        shift.FinishTime = new TimeOnly(18, 0);
        currentDemand.Columns.Single(column => column.Position == 0).TargetSales = 2000m;
        await db.SaveChangesAsync();
        var updated = await service.GetAsync(Monday, default);
        Assert.Equal(120m, updated.Drivers.LabourCost);
        Assert.Equal(6m, updated.Drivers.ScheduledHours);
        Assert.Equal(8000m, updated.TargetSales);
        Assert.Equal(6m, updated.Days[0].Drivers.LabourPercentage);

        db.RosterShifts.Remove(shift);
        await db.SaveChangesAsync();
        Assert.Equal(0m, (await service.GetAsync(Monday, default)).Drivers.LabourCost);
    }

    [Fact]
    public async Task EmptySavedDriverRosterIsCompleteWithoutAnInsideRoster()
    {
        using var db = NewDb();
        Plan(db, RosterKinds.Drivers);
        Demand(db, 1000m);
        await db.SaveChangesAsync();
        var service = new RosterLabourService(db);

        var partial = await service.GetAsync(Monday, default);
        Assert.True(partial.HasDriverRoster);
        Assert.True(partial.Drivers.IsComplete);
        Assert.Equal(0m, partial.Drivers.LabourPercentage);
        Assert.Empty(partial.Warnings);

        Plan(db, RosterKinds.Inside);
        await db.SaveChangesAsync();
        var complete = await service.GetAsync(Monday, default);
        Assert.True(complete.Drivers.IsComplete);
        Assert.Equal(0m, complete.Drivers.LabourCost);
        Assert.Equal(0m, complete.Drivers.LabourPercentage);
        Assert.Empty(complete.Warnings);
    }

    [Fact]
    public async Task MissingDriverRosterAndSalesReturnsExplicitUnavailableLabour()
    {
        using var db = NewDb();
        var result = await new RosterLabourService(db).GetAsync(Monday, default);
        Assert.False(result.HasDriverRoster);
        Assert.False(result.Drivers.IsComplete);
        Assert.Equal(0m, result.Drivers.LabourCost);
        Assert.Null(result.TargetSales);
        Assert.Null(result.Drivers.LabourPercentage);
        Assert.Equal(7, result.Days.Count);
        Assert.All(result.Days, day => Assert.Null(day.TargetSales));
        Assert.Equal(2, result.Warnings.Count);
    }

    [Fact]
    public async Task ArchivedInsideRosterNeverAppearsInLabourOrMakesDriverRosterComplete()
    {
        using var db = NewDb();
        Shift(Plan(db, RosterKinds.Inside), Employee(db, 100m, manager: true), Monday, 12, 20);
        Demand(db, 1000m);
        await db.SaveChangesAsync();

        var result = await new RosterLabourService(db).GetAsync(Monday, default);
        Assert.False(result.HasDriverRoster);
        Assert.False(result.Drivers.IsComplete);
        Assert.Equal(0m, result.Drivers.LabourCost);
        Assert.Null(result.Drivers.LabourPercentage);
        Assert.All(result.Days, day => Assert.Equal(0m, day.Drivers.ScheduledHours));
        var json = System.Text.Json.JsonSerializer.Serialize(result,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.DoesNotContain("inside", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("combined", json, StringComparison.OrdinalIgnoreCase);
        Assert.Single(await db.RosterPlans.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ZeroOrMissingSalesLeavesCostAvailableAndPercentageUndefined(bool missingColumn)
    {
        using var db = NewDb();
        var plan = Plan(db, RosterKinds.Drivers);
        Shift(plan, Employee(db, 14.5m), Monday, 12, 15);
        var demand = Demand(db, 0m);
        if (missingColumn) demand.Columns.Remove(demand.Columns.First());
        await db.SaveChangesAsync();

        var result = await new RosterLabourService(db).GetAsync(Monday, default);
        Assert.Equal(43.5m, result.Drivers.LabourCost);
        Assert.Null(result.Drivers.LabourPercentage);
        Assert.All(result.Days, day => Assert.Null(day.Drivers.LabourPercentage));
        Assert.Equal(missingColumn ? null : (decimal?)0, result.TargetSales);
    }

    [Fact]
    public async Task InactiveEmployeesSavedHoursStillContributeToActualLabour()
    {
        using var db = NewDb();
        var plan = Plan(db, RosterKinds.Drivers);
        var employee = Employee(db, 14.5m);
        employee.IsActive = false;
        Shift(plan, employee, Monday, 12, 15);
        await db.SaveChangesAsync();
        var result = await new RosterLabourService(db).GetAsync(Monday, default);
        Assert.Equal(43.5m, result.Drivers.LabourCost);
    }

    [Fact]
    public async Task FractionalHoursAreCostedWithoutWholeHourTruncation()
    {
        using var db = NewDb();
        var plan = Plan(db, RosterKinds.Drivers);
        var shift = Shift(plan, Employee(db, 20m), Monday.AddDays(5), 23, 2);
        shift.StartTime = new TimeOnly(23, 30);
        shift.FinishTime = new TimeOnly(2, 30);
        await db.SaveChangesAsync();
        var result = await new RosterLabourService(db).GetAsync(Monday, default);
        Assert.Equal(3m, result.Drivers.ScheduledHours);
        Assert.Equal(72.5m, result.Drivers.LabourCost);
    }

    [Fact]
    public async Task LabourQueryRequiresValidWeekAndUsesServerWeekSelection()
    {
        using var db = NewDb();
        var controller = new AdminRosterController(new WeekSelectionRequestValidator(),
            new RosterPlanUpdateRequestValidator(), new RosterSettingsRequestValidator(),
            new RosterLabourRequestValidator(), null!, null!, new RosterLabourService(db), null!);
        Assert.IsType<BadRequestObjectResult>(await controller.GetLabour(new() { WeekStart = Monday.AddDays(1) }, default));
        Assert.IsType<BadRequestObjectResult>(await controller.GetLabour(new() { WeekStart = DateOnly.MinValue }, default));
        Assert.IsType<BadRequestObjectResult>(await controller.GetLabour(new() { WeekOffset = 4 }, default));
        Assert.IsType<BadRequestObjectResult>(await controller.GetLabour(new() { WeekStart = Monday, WeekOffset = 1 }, default));
        var selected = Assert.IsType<OkObjectResult>(await controller.GetLabour(new() { WeekOffset = 2 }, default));
        Assert.Equal(WeeklyScheduleService.GetWeekMonday(2), Assert.IsType<RosterLabourResponse>(selected.Value).WeekStart);
        var past = Assert.IsType<OkObjectResult>(await controller.GetLabour(new() { WeekStart = Monday.AddDays(-28) }, default));
        Assert.Equal(Monday.AddDays(-28), Assert.IsType<RosterLabourResponse>(past.Value).WeekStart);
    }

    [Fact]
    public void LabourReadEndpointRetainsManagementPolicy()
    {
        Assert.Contains(typeof(AdminRosterController).GetCustomAttributes(true),
            attribute => attribute is AuthorizeAttribute { Policy: AuthorizationPolicies.Manager });
        var method = typeof(AdminRosterController).GetMethod(nameof(AdminRosterController.GetLabour))!;
        Assert.DoesNotContain(method.GetCustomAttributes(true), attribute => attribute is AllowAnonymousAttribute);
        Assert.DoesNotContain(method.GetCustomAttributes(true),
            attribute => attribute is AuthorizeAttribute { Policy: AuthorizationPolicies.Admin });
    }

    private static AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static Employee Employee(AppDbContext db, decimal rate, bool manager = false)
    {
        var employee = new Employee
        {
            Id = Guid.NewGuid(), EmployeeNumber = Guid.NewGuid().ToString(), FirstName = "Test", LastName = "Employee",
            PhoneNumber = "000", HourlyRate = rate
        };
        if (manager) employee.ManagerProfile = new ManagerProfile { EmployeeId = employee.Id, Employee = employee };
        db.Employees.Add(employee);
        return employee;
    }

    private static RosterPlan Plan(AppDbContext db, string kind, DateOnly? week = null)
    {
        var plan = new RosterPlan { Id = Guid.NewGuid(), RosterKind = kind, WeekStart = week ?? Monday };
        db.RosterPlans.Add(plan);
        return plan;
    }

    private static RosterShift Shift(RosterPlan plan, Employee employee, DateOnly date, int start, int finish)
    {
        var shift = new RosterShift
        {
            Id = Guid.NewGuid(), RosterPlanId = plan.Id, RosterPlan = plan, EmployeeId = employee.Id,
            Employee = employee, Date = date, StartTime = new TimeOnly(start, 0), FinishTime = new TimeOnly(finish, 0)
        };
        plan.Shifts.Add(shift);
        return shift;
    }

    private static DemandPlan Demand(AppDbContext db, decimal dailySales)
    {
        var plan = new DemandPlan
        {
            Id = Guid.NewGuid(), Name = "Test template", WeekStart = Monday, UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        plan.Columns = Enumerable.Range(0, 7).Select(position => new DemandColumn
        {
            Id = Guid.NewGuid(), DemandPlanId = plan.Id, DemandPlan = plan, Position = position,
            Label = Monday.AddDays(position).DayOfWeek.ToString(), TargetSales = dailySales
        }).ToList();
        db.DemandPlans.Add(plan);
        return plan;
    }

    private static void AddUser(AppDbContext db, Employee employee, string roleName)
    {
        var role = new IdentityRole<Guid>(roleName) { Id = Guid.NewGuid(), NormalizedName = roleName.ToUpperInvariant() };
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), EmployeeId = employee.Id, Employee = employee, UserName = employee.EmployeeNumber
        };
        employee.User = user;
        db.Roles.Add(role);
        db.Users.Add(user);
        db.UserRoles.Add(new IdentityUserRole<Guid> { RoleId = role.Id, UserId = user.Id });
    }
}
