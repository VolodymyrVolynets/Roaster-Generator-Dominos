using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Roaster_Generator.Configuration;
using Roaster_Generator.Contracts.Roster;
using Roaster_Generator.Contracts.Schedules;
using Roaster_Generator.Controllers;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Security;
using Roaster_Generator.Services;
using Roaster_Generator.Validation;

namespace Roaster_Generator.Tests;

public sealed class DriverOnlyRosterTests
{
    [Theory]
    [InlineData("read")]
    [InlineData("summary")]
    [InlineData("generate")]
    [InlineData("cancel")]
    [InlineData("history")]
    [InlineData("jobs")]
    [InlineData("edit")]
    public async Task InsideRosterHttpEndpointsRejectRequestsBeforeAccessingServices(string endpoint)
    {
        var controller = new AdminRosterController(new WeekSelectionRequestValidator(),
            new RosterPlanUpdateRequestValidator(), null!, null!, null!, null!, null!, null!);

        var result = endpoint switch
        {
            "read" => await controller.Get(1, null, default, RosterKinds.Inside),
            "summary" => await controller.GetSummary(1, default, RosterKinds.Inside),
            "generate" => await controller.StartTimer(new WeekSelectionRequest { WeekOffset = 1 }, default, RosterKinds.Inside),
            "cancel" => await controller.Cancel(new RosterTimerCancelRequest { WeekOffset = 1 }, default, RosterKinds.Inside),
            "history" => await controller.History(default, RosterKinds.Inside),
            "jobs" => await controller.Jobs(1, default, RosterKinds.Inside),
            "edit" => await controller.Update(new RosterPlanUpdateRequest
            {
                WeekStart = WeeklyScheduleService.GetWeekMonday(1), RosterKind = RosterKinds.Inside
            }, default),
            _ => throw new ArgumentOutOfRangeException(nameof(endpoint))
        };

        var error = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains(RosterKinds.DisabledMessage, JsonSerializer.Serialize(error.Value));
    }

    [Theory]
    [InlineData("load")]
    [InlineData("read")]
    [InlineData("read-offset")]
    [InlineData("summary")]
    [InlineData("history")]
    [InlineData("edit")]
    [InlineData("save")]
    public async Task InsideRosterServiceEntryPointsRejectRequestsWithoutReadingOrWritingTheDatabase(string operation)
    {
        var inputs = new RosterInputService(null!, null!, null!);
        var plans = new RosterPlanService(null!, inputs);
        var monday = WeeklyScheduleService.GetWeekMonday(1);
        var input = new RosterSolverInput(monday, [], [], [], [], new(), RosterKind: RosterKinds.Inside);

        var exception = await Assert.ThrowsAsync<RosterInputException>(async () =>
        {
            switch (operation)
            {
                case "load": await inputs.LoadAsync(monday, default, RosterKinds.Inside); break;
                case "read": await plans.GetAsync(monday, default, RosterKinds.Inside); break;
                case "read-offset": await plans.GetAsync(1, default, RosterKinds.Inside); break;
                case "summary": await plans.GetSummaryAsync(1, default, RosterKinds.Inside); break;
                case "history": await plans.GetHistoryAsync(default, RosterKinds.Inside); break;
                case "edit": await plans.UpdateAsync(new RosterPlanUpdateRequest { WeekStart = monday, RosterKind = RosterKinds.Inside }, default); break;
                case "save": await plans.SaveAsync(new LoadedRosterInput(input, new(), "test", []), new RosterSolverResult { Status = "optimal" }, default); break;
            }
        });

        Assert.Equal(RosterKinds.DisabledMessage, exception.Message);
    }

    [Fact]
    public void DisabledGenerationDoesNotOccupyTheDriverWorkerOrExposeJobLogs()
    {
        using var timer = new RosterTimerService(null!, null!, NullLogger<RosterTimerService>.Instance);
        Assert.Throws<RosterInputException>(() => timer.Start(1, RosterKinds.Inside));
        Assert.Throws<RosterInputException>(() => timer.GetLogs(WeeklyScheduleService.GetWeekMonday(1), RosterKinds.Inside));
        Assert.Throws<RosterInputException>(() => timer.Cancel(1, rosterKind: RosterKinds.Inside));

        var driverJob = timer.Start(1);
        Assert.Equal(RosterKinds.Drivers, driverJob.RosterKind);
        Assert.Single(timer.GetLogs(driverJob.WeekStart));
        Assert.True(timer.Cancel(1, driverJob.JobId));
    }

    [Fact]
    public async Task DriverInputSummaryAndAvailabilityExcludeInsideRolesAndStaleProfiles()
    {
        using var db = NewDb();
        var driver = AddEmployee(db, [RoleNames.Driver]);
        AddEmployee(db, [RoleNames.InStore]);
        AddEmployee(db, [RoleNames.Manager]);
        AddEmployee(db, [RoleNames.Driver, RoleNames.InStore]);
        AddEmployee(db, [RoleNames.Driver, RoleNames.Manager]);
        AddEmployee(db, [RoleNames.Driver], active: false);
        AddEmployee(db, [], driverProfile: true);
        AddEmployee(db, [RoleNames.Driver], driverProfile: false);
        // Mismatched roles/profiles remain excluded in either direction.
        var staleManager = AddEmployee(db, [RoleNames.Driver]);
        staleManager.ManagerProfile = new ManagerProfile();
        AddEmployee(db, [RoleNames.Driver, RoleNames.Manager], managerProfile: false);
        AddDemand(db);
        await db.SaveChangesAsync();

        var inputs = NewInputs(db);
        var loaded = await inputs.LoadAsync(WeeklyScheduleService.GetWeekMonday(1), default);
        Assert.Equal(driver.Id, Assert.Single(loaded.Input.Employees).Id);
        Assert.Equal(driver.Id, Assert.Single(loaded.Input.Availability).EmployeeId);
        Assert.All(loaded.Input.Demand, slot => Assert.Equal(0, slot.RequiredDrivers));

        var overview = await new WeeklyScheduleService(db).GetWeekForAllAsync(1, default);
        Assert.Equal(driver.Id, Assert.Single(overview.Employees).EmployeeId);
        var summary = await new RosterPlanService(db, inputs).GetSummaryAsync(1, default);
        Assert.Equal(6, summary.EnteredAvailabilityHours);
        Assert.Equal(0, summary.DriversWithoutAvailability);
    }

    [Fact]
    public async Task SavedReadsAndFairnessUseOnlyDriverRostersAndLeaveLegacyInsideDataStored()
    {
        using var db = NewDb();
        var driver = AddEmployee(db, [RoleNames.Driver]);
        AddDemand(db);
        var monday = WeeklyScheduleService.GetWeekMonday(1);
        var currentDriver = AddPlan(db, RosterKinds.Drivers, monday, driver, 6);
        AddPlan(db, RosterKinds.Inside, monday, driver, 70);
        var priorDriver = AddPlan(db, RosterKinds.Drivers, monday.AddDays(-7), driver, 12);
        var priorInside = AddPlan(db, RosterKinds.Inside, monday.AddDays(-7), driver, 100);
        AddBoundaryShift(priorDriver, driver, monday.AddDays(-1), 18, 0);
        AddBoundaryShift(priorInside, driver, monday.AddDays(-1), 20, 2);
        await db.SaveChangesAsync();

        var inputs = NewInputs(db);
        var plans = new RosterPlanService(db, inputs);
        var saved = await plans.GetAsync(monday, default);
        Assert.Equal(currentDriver.Id, saved!.Id);
        Assert.Equal(6, saved.TotalScheduledHours);
        var history = JsonSerializer.SerializeToElement(await plans.GetHistoryAsync(default));
        Assert.Equal(2, history.GetArrayLength());
        Assert.All(history.EnumerateArray(), item => Assert.Equal(RosterKinds.Drivers, item.GetProperty("RosterKind").GetString()));

        var loaded = await inputs.LoadAsync(monday, default);
        Assert.Equal(12, Assert.Single(loaded.Input.History!).ScheduledHours);
        var boundary = Assert.Single(loaded.Input.BoundaryShifts);
        Assert.Equal(monday.ToDateTime(TimeOnly.MinValue), boundary.Finish);
        Assert.Equal(4, await db.RosterPlans.CountAsync());
        Assert.Equal(2, await db.RosterPlans.CountAsync(plan => plan.RosterKind == RosterKinds.Inside));
    }

    private static AppDbContext NewDb()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Database.EnsureCreated();
        return db;
    }

    private static RosterInputService NewInputs(AppDbContext db) =>
        new(db, new RosterSettingsService(db), Options.Create(new ShopHoursOptions()));

    private static Employee AddEmployee(AppDbContext db, string[] roles, bool active = true,
        bool? driverProfile = null, bool? managerProfile = null)
    {
        var employee = new Employee
        {
            Id = Guid.NewGuid(), IsActive = active, FirstName = "Test", LastName = "Employee",
            DriverProfile = (driverProfile ?? roles.Contains(RoleNames.Driver)) ? new DriverProfile { TargetHours = 20 } : null,
            ManagerProfile = (managerProfile ?? roles.Contains(RoleNames.Manager)) ? new ManagerProfile() : null,
            InStoreProfile = roles.Contains(RoleNames.InStore) ? new InStoreProfile() : null
        };
        db.Employees.Add(employee);
        var user = new ApplicationUser { Id = Guid.NewGuid(), EmployeeId = employee.Id, UserName = employee.Id.ToString() };
        db.Users.Add(user);
        foreach (var name in roles)
        {
            var role = db.Roles.Local.SingleOrDefault(role => role.Name == name);
            if (role is null)
            {
                role = new IdentityRole<Guid> { Id = Guid.NewGuid(), Name = name, NormalizedName = name.ToUpperInvariant() };
                db.Roles.Add(role);
            }
            db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = role.Id });
        }
        db.Shifts.Add(new Shift
        {
            Id = Guid.NewGuid(), EmployeeId = employee.Id, Date = WeeklyScheduleService.GetWeekMonday(1),
            StartTime = new TimeOnly(12, 0), FinishTime = new TimeOnly(18, 0)
        });
        return employee;
    }

    private static void AddDemand(AppDbContext db)
    {
        var plan = new DemandPlan { Id = Guid.NewGuid(), WeekStart = WeeklyScheduleService.GetWeekMonday(1) };
        for (var day = 0; day < 7; day++)
            plan.Columns.Add(new DemandColumn { Id = Guid.NewGuid(), Position = day, Label = $"Day {day}" });
        for (var hour = 12; hour < 25; hour++)
        {
            var row = new DemandRow { Id = Guid.NewGuid(), Hour = hour % 24 };
            foreach (var column in plan.Columns)
                row.Values.Add(new DemandValue { Id = Guid.NewGuid(), DemandColumnId = column.Id, Demand = 0 });
            plan.Rows.Add(row);
        }
        db.DemandPlans.Add(plan);
    }

    private static RosterPlan AddPlan(AppDbContext db, string kind, DateOnly week, Employee employee, int hours)
    {
        var plan = new RosterPlan { Id = Guid.NewGuid(), WeekStart = week, RosterKind = kind };
        plan.SnapshotJson = JsonSerializer.Serialize(new RosterPlanResponse
        {
            Id = plan.Id, WeekStart = week, RosterKind = kind, TotalScheduledHours = hours,
            Employees = [new RosterEmployeeResponse { EmployeeId = employee.Id, TargetHours = 20, ScheduledHours = hours }]
        });
        db.RosterPlans.Add(plan);
        return plan;
    }

    private static void AddBoundaryShift(RosterPlan plan, Employee employee, DateOnly date, int start, int finish) =>
        plan.Shifts.Add(new RosterShift
        {
            Id = Guid.NewGuid(), EmployeeId = employee.Id, Date = date,
            StartTime = new TimeOnly(start, 0), FinishTime = new TimeOnly(finish, 0)
        });
}
