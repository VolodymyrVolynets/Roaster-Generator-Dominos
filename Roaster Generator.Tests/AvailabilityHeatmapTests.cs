using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Roaster_Generator.Configuration;
using Roaster_Generator.Data;
using Roaster_Generator.Contracts.Schedules;
using Roaster_Generator.Entities;
using Roaster_Generator.Enums;
using Roaster_Generator.Security;
using Roaster_Generator.Services;

namespace Roaster_Generator.Tests;

public sealed class AvailabilityHeatmapTests
{
    [Fact]
    public async Task DriverAndAdminAvailabilityExposeTheSameHoursUsedByRosterInputs()
    {
        using var db = NewDb();
        var monday = WeeklyScheduleService.GetWeekMonday(1);
        var broad = AddDriver(db, "Broad");
        var limited = AddDriver(db, "Limited");
        AddAvailability(db, broad, monday, 12, 18);
        AddAvailability(db, limited, monday, 12, 14);
        AddCompleteDemand(db, monday);
        await db.SaveChangesAsync();
        var inputs = new RosterInputService(
            db,
            new RosterSettingsService(db),
            Options.Create(new ShopHoursOptions()));
        var service = new WeeklyScheduleService(db, inputs);

        var overview = await service.GetWeekForAllAsync(1, default);
        var loaded = await inputs.LoadAsync(monday, default);
        var broadAllocation = loaded.Input.ExpectedHoursByEmployee![broad.Id];
        var limitedAllocation = loaded.Input.ExpectedHoursByEmployee[limited.Id];

        Assert.Equal(broadAllocation.ExpectedHours,
            overview.Employees.Single(employee => employee.EmployeeId == broad.Id).ApproximateHours);
        Assert.Equal(limitedAllocation.ExpectedHours,
            overview.Employees.Single(employee => employee.EmployeeId == limited.Id).ApproximateHours);
        Assert.Equal(6d, overview.Employees.Sum(employee => employee.ApproximateHours ?? 0), 4);
        Assert.True(broadAllocation.ExpectedHours > limitedAllocation.ExpectedHours);

        var personal = await service.GetWeekAsync(limited.Id, 1, default);
        Assert.Equal(limitedAllocation.ExpectedHours, personal!.ApproximateHours);
        Assert.Equal(limitedAllocation.CapacityHours, personal.ApproximateCapacityHours);
        Assert.NotNull(personal.Heatmap);
    }

    [Fact]
    public async Task WeeklyOverviewClassifiesDemandByCurrentDriverAvailability()
    {
        using var db = NewDb();
        var monday = WeeklyScheduleService.GetWeekMonday(1);
        var first = AddDriver(db, "First");
        var second = AddDriver(db, "Second");
        var third = AddDriver(db, "Third");
        AddAvailability(db, first, monday, 12, 16);
        AddAvailability(db, second, monday, 13, 16);
        AddAvailability(db, third, monday, 14, 16);
        AddDemand(db, monday, DemandKinds.Outside, (12, 2), (13, 2), (14, 2), (15, 1));
        AddDemand(db, monday, DemandKinds.Inside, (16, 9));
        await db.SaveChangesAsync();

        var response = await new WeeklyScheduleService(db, null).GetWeekForAllAsync(1, default);

        Assert.True(response.Heatmap.DemandPlanExists);
        Assert.Equal(4, response.Heatmap.Slots.Count);
        AssertSlot(response, 12, required: 2, available: 1, shortage: 1, level: "shortage", scarcity: 2);
        AssertSlot(response, 13, required: 2, available: 2, shortage: 0, level: "tight", scarcity: 1);
        AssertSlot(response, 14, required: 2, available: 3, shortage: 0, level: "limited", scarcity: 0.67);
        AssertSlot(response, 15, required: 1, available: 3, shortage: 0, level: "covered", scarcity: 0.33);
        Assert.DoesNotContain(response.Heatmap.Slots, slot => slot.Hour == 16);
    }

    [Fact]
    public async Task HeatmapUsesBusinessDayOvernightHoursAndApprovedSickLeave()
    {
        using var db = NewDb();
        var monday = WeeklyScheduleService.GetWeekMonday(1);
        var working = AddDriver(db, "Working");
        var sick = AddDriver(db, "Sick");
        AddAvailability(db, working, monday, 20, 2);
        AddAvailability(db, sick, monday, 20, 2);
        db.SickLeaveRequests.Add(new SickLeaveRequest
        {
            Id = Guid.NewGuid(), EmployeeId = sick.Id, StartDate = monday, FinishDate = monday,
            Status = SickLeaveStatus.Approved
        });
        AddDemand(db, monday, DemandKinds.Outside, (0, 2));
        await db.SaveChangesAsync();

        var response = await new WeeklyScheduleService(db, null).GetWeekForAllAsync(1, default);
        var slot = Assert.Single(response.Heatmap.Slots);

        Assert.Equal(24, slot.Hour);
        Assert.Equal("00:00", slot.StartTime);
        Assert.Equal(1, slot.StartDayOffset);
        Assert.Equal(1, slot.AvailableDrivers);
        Assert.Equal(1, slot.ShortageDrivers);
    }

    [Fact]
    public async Task PersonalScheduleIncludesHeatmapOnlyForEligibleDrivers()
    {
        using var db = NewDb();
        var monday = WeeklyScheduleService.GetWeekMonday(1);
        var driver = AddDriver(db, "Driver");
        var inStore = new Employee { Id = Guid.NewGuid(), IsActive = true, FirstName = "Inside" };
        db.Employees.Add(inStore);
        AddDemand(db, monday, DemandKinds.Outside, (12, 1));
        await db.SaveChangesAsync();
        var service = new WeeklyScheduleService(db, null);

        var driverSchedule = await service.GetWeekAsync(driver.Id, 1, default);
        var inStoreSchedule = await service.GetWeekAsync(inStore.Id, 1, default);

        Assert.NotNull(driverSchedule!.Heatmap);
        Assert.Null(inStoreSchedule!.Heatmap);
    }

    [Fact]
    public async Task MissingWeeklyDemandReturnsAnExplanatoryEmptyHeatmap()
    {
        using var db = NewDb();

        var response = await new WeeklyScheduleService(db, null).GetWeekForAllAsync(1, default);

        Assert.False(response.Heatmap.DemandPlanExists);
        Assert.Empty(response.Heatmap.Slots);
        Assert.Contains("not been entered", response.Heatmap.Message);
    }

    [Fact]
    public async Task SavingAvailabilityReturnsTheImmediatelyRefreshedHeatmap()
    {
        using var db = NewDb();
        var monday = WeeklyScheduleService.GetWeekMonday(1);
        var driver = AddDriver(db, "Saving");
        AddDemand(db, monday, DemandKinds.Outside, (12, 1));
        await db.SaveChangesAsync();
        var service = new WeeklyScheduleService(db, null);
        var before = await service.GetWeekAsync(driver.Id, 1, default);

        var saved = await service.ReplaceWeekAsync(driver.Id, new WeeklyScheduleRequest
        {
            WeekOffset = 1,
            Days =
            [
                new ScheduleDayRequest
                {
                    Date = monday, StartTime = new TimeOnly(12, 0), FinishTime = new TimeOnly(13, 0)
                }
            ]
        }, default);

        Assert.Equal(0, Assert.Single(before!.Heatmap!.Slots).AvailableDrivers);
        Assert.Equal(1, Assert.Single(saved!.Heatmap!.Slots).AvailableDrivers);
        Assert.Equal("tight", Assert.Single(saved.Heatmap.Slots).Level);
    }

    private static void AssertSlot(WeeklyAvailabilityResponse response,
        int hour, int required, int available, int shortage, string level, double scarcity)
    {
        var slot = response.Heatmap.Slots.Single(item => item.Hour == hour);
        Assert.Equal(required, slot.RequiredDrivers);
        Assert.Equal(available, slot.AvailableDrivers);
        Assert.Equal(shortage, slot.ShortageDrivers);
        Assert.Equal(level, slot.Level);
        Assert.Equal(scarcity, slot.ScarcityScore, 2);
    }

    private static AppDbContext NewDb()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Database.EnsureCreated();
        return db;
    }

    private static Employee AddDriver(AppDbContext db, string firstName)
    {
        var employee = new Employee
        {
            Id = Guid.NewGuid(), IsActive = true, FirstName = firstName,
            DriverProfile = new DriverProfile { DriverType = DriverType.Car }
        };
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), EmployeeId = employee.Id, UserName = $"{firstName}-{employee.Id}"
        };
        var role = db.Roles.Local.SingleOrDefault(item => item.Name == RoleNames.Driver);
        if (role is null)
        {
            role = new IdentityRole<Guid>
            {
                Id = Guid.NewGuid(), Name = RoleNames.Driver, NormalizedName = RoleNames.Driver.ToUpperInvariant()
            };
            db.Roles.Add(role);
        }
        db.Employees.Add(employee);
        db.Users.Add(user);
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = role.Id });
        return employee;
    }

    private static void AddAvailability(AppDbContext db, Employee employee, DateOnly date, int start, int finish) =>
        db.Shifts.Add(new Shift
        {
            Id = Guid.NewGuid(), EmployeeId = employee.Id, Date = date,
            StartTime = new TimeOnly(start, 0), FinishTime = new TimeOnly(finish, 0)
        });

    private static void AddDemand(AppDbContext db, DateOnly weekStart, string kind,
        params (int Hour, int Required)[] values)
    {
        var plan = new DemandPlan { Id = Guid.NewGuid(), WeekStart = weekStart, DemandKind = kind };
        var columns = Enumerable.Range(0, 7).Select(position => new DemandColumn
        {
            Id = Guid.NewGuid(), DemandPlanId = plan.Id, Position = position, Label = $"Day {position}"
        }).ToArray();
        foreach (var (hour, required) in values)
        {
            var row = new DemandRow { Id = Guid.NewGuid(), DemandPlanId = plan.Id, Hour = hour };
            row.Values.Add(new DemandValue
            {
                Id = Guid.NewGuid(), DemandRowId = row.Id, DemandColumnId = columns[0].Id, Demand = required
            });
            plan.Rows.Add(row);
        }
        foreach (var column in columns) plan.Columns.Add(column);
        db.DemandPlans.Add(plan);
    }

    private static void AddCompleteDemand(AppDbContext db, DateOnly weekStart)
    {
        var plan = new DemandPlan
        {
            Id = Guid.NewGuid(), WeekStart = weekStart, DemandKind = DemandKinds.Outside
        };
        var columns = Enumerable.Range(0, 7).Select(position => new DemandColumn
        {
            Id = Guid.NewGuid(), DemandPlanId = plan.Id, Position = position, Label = $"Day {position}"
        }).ToArray();
        foreach (var sourceHour in Enumerable.Range(12, 13))
        {
            var row = new DemandRow
            {
                Id = Guid.NewGuid(), DemandPlanId = plan.Id, Hour = sourceHour % 24
            };
            foreach (var column in columns)
                row.Values.Add(new DemandValue
                {
                    Id = Guid.NewGuid(), DemandRowId = row.Id, DemandColumnId = column.Id,
                    Demand = column.Position == 0 && sourceHour < 18 ? 1 : 0
                });
            plan.Rows.Add(row);
        }
        foreach (var column in columns) plan.Columns.Add(column);
        db.DemandPlans.Add(plan);
    }
}
