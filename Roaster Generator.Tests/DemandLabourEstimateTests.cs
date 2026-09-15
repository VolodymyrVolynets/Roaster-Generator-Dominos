using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Roaster_Generator.Configuration;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Enums;
using Roaster_Generator.Security;
using Roaster_Generator.Services;

namespace Roaster_Generator.Tests;

public sealed class DemandLabourEstimateTests
{
    private static readonly DateOnly Monday = new(2026, 9, 14);

    [Fact]
    public async Task OutsideDemandWeightsPayByAutomaticApproximateHours()
    {
        using var db = NewDb();
        var cheap = AddDriver(db, "Cheap", 10m);
        var expensive = AddDriver(db, "Expensive", 20m);
        AddAvailability(db, cheap, 12, 14);
        AddAvailability(db, expensive, 12, 16);
        var plan = AddDemand(db, DemandKinds.Outside, hour => hour is >= 12 and < 16 ? 1 : 0);
        await db.SaveChangesAsync();

        var response = await Service(db).GetPlanAsync(plan.Id, default);
        var estimate = Assert.IsType<Roaster_Generator.Contracts.Demand.DemandLabourEstimateResponse>(
            response!.LabourEstimate);

        Assert.True(estimate.IsAvailable);
        Assert.Equal(4, estimate.TotalDemandHours, 4);
        Assert.Equal(4, estimate.TotalApproximateHours, 4);
        Assert.Equal(0, estimate.UnallocatedDemandHours, 4);
        Assert.Equal(2, estimate.Drivers.Count);
        var cheapHours = estimate.Drivers.Single(item => item.EmployeeId == cheap.Id).ApproximateHours;
        var expensiveHours = estimate.Drivers.Single(item => item.EmployeeId == expensive.Id).ApproximateHours;
        Assert.True(expensiveHours > cheapHours);
        var expectedRate =
            ((decimal)cheapHours * cheap.HourlyRate + (decimal)expensiveHours * expensive.HourlyRate) /
            (decimal)(cheapHours + expensiveHours);
        Assert.Equal(expectedRate, estimate.WeightedAverageHourlyRate);
        Assert.NotEqual(15m, estimate.WeightedAverageHourlyRate);
        Assert.Equal(Math.Round((decimal)cheapHours * cheap.HourlyRate, 2, MidpointRounding.AwayFromZero),
            estimate.Drivers.Single(item => item.EmployeeId == cheap.Id).ApproximateBaseCost);
    }

    [Fact]
    public async Task OutsideDemandExplainsWhenNoUsefulAvailabilityCanBePriced()
    {
        using var db = NewDb();
        AddDriver(db, "Unavailable", 17m);
        var plan = AddDemand(db, DemandKinds.Outside, hour => hour == 12 ? 1 : 0);
        await db.SaveChangesAsync();

        var response = await Service(db).GetPlanAsync(plan.Id, default);
        var estimate = response!.LabourEstimate!;

        Assert.False(estimate.IsAvailable);
        Assert.Equal(1, estimate.UnallocatedDemandHours, 4);
        Assert.Null(estimate.WeightedAverageHourlyRate);
        Assert.Null(estimate.ApproximateBaseLabourCost);
        Assert.Contains("No useful driver availability", estimate.Message);
    }

    [Fact]
    public async Task InsideDemandDoesNotReuseDriverApproximateHours()
    {
        using var db = NewDb();
        AddDriver(db, "Driver", 20m);
        var plan = AddDemand(db, DemandKinds.Inside, _ => 1);
        await db.SaveChangesAsync();

        var response = await Service(db).GetPlanAsync(plan.Id, default);

        Assert.Null(response!.LabourEstimate);
    }

    private static AppDbContext NewDb()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Database.EnsureCreated();
        return db;
    }

    private static DemandService Service(AppDbContext db)
    {
        var options = Options.Create(new ShopHoursOptions());
        var inputs = new RosterInputService(db, new RosterSettingsService(db), options);
        return new DemandService(db, options, inputs);
    }

    private static Employee AddDriver(AppDbContext db, string firstName, decimal hourlyRate)
    {
        var employee = new Employee
        {
            Id = Guid.NewGuid(), IsActive = true, FirstName = firstName, HourlyRate = hourlyRate,
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

    private static void AddAvailability(AppDbContext db, Employee employee, int start, int finish) =>
        db.Shifts.Add(new Shift
        {
            Id = Guid.NewGuid(), EmployeeId = employee.Id, Date = Monday,
            StartTime = new TimeOnly(start, 0), FinishTime = new TimeOnly(finish, 0)
        });

    private static DemandPlan AddDemand(AppDbContext db, string kind, Func<int, int> required)
    {
        var plan = new DemandPlan
        {
            Id = Guid.NewGuid(), Name = "Labour", WeekStart = Monday, DemandKind = kind
        };
        var columns = Enumerable.Range(0, 7).Select(position => new DemandColumn
        {
            Id = Guid.NewGuid(), DemandPlanId = plan.Id, Position = position, Label = $"Day {position}"
        }).ToArray();
        foreach (var sourceHour in Enumerable.Range(12, 13))
        {
            var hour = sourceHour % 24;
            var row = new DemandRow { Id = Guid.NewGuid(), DemandPlanId = plan.Id, Hour = hour };
            foreach (var column in columns)
            {
                var demand = column.Position == 0 ? required(sourceHour) : 0;
                row.Values.Add(new DemandValue
                {
                    Id = Guid.NewGuid(), DemandRowId = row.Id, DemandColumnId = column.Id,
                    Demand = kind == DemandKinds.Outside ? demand : null,
                    InsideDemand = kind == DemandKinds.Inside ? demand : null
                });
            }
            plan.Rows.Add(row);
        }
        foreach (var column in columns) plan.Columns.Add(column);
        db.DemandPlans.Add(plan);
        return plan;
    }
}
