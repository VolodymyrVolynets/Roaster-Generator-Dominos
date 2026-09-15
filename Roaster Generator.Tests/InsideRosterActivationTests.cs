using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Roaster_Generator.Configuration;
using Roaster_Generator.Controllers;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Enums;
using Roaster_Generator.Security;
using Roaster_Generator.Services;
using Roaster_Generator.Validation;

namespace Roaster_Generator.Tests;

public sealed class InsideRosterActivationTests
{
    private static readonly DateOnly Monday = new(2026, 9, 14);

    [Fact]
    public async Task InsideInputUsesIndependentDemandAndEligibleManagerAndInStoreRoles()
    {
        using var db = NewDb();
        var driver = AddEmployee(db, [RoleNames.Driver]);
        var manager = AddEmployee(db, [RoleNames.Manager, RoleNames.Driver]);
        var inStore = AddEmployee(db, [RoleNames.InStore]);
        inStore.ManagerProfile = new ManagerProfile();
        AddDemand(db, DemandKinds.Outside, 1);
        AddDemand(db, DemandKinds.Inside, 2);
        await db.SaveChangesAsync();

        var inputs = Inputs(db);
        var inside = await inputs.LoadAsync(Monday, default, RosterKinds.Inside);
        var outside = await inputs.LoadAsync(Monday, default, RosterKinds.Drivers);

        Assert.Equal(new[] { manager.Id, inStore.Id }.Order(), inside.Input.Employees.Select(employee => employee.Id).Order());
        Assert.Null(inside.Input.Employees.Single(employee => employee.Id == inStore.Id).ManagerProfile);
        Assert.All(inside.Input.Demand, slot => Assert.Equal(2, slot.RequiredDrivers));
        Assert.Equal(inside.Input.Employees.Count, inside.Input.ExpectedHoursByEmployee!.Count);
        Assert.Equal(driver.Id, Assert.Single(outside.Input.Employees).Id);
        Assert.All(outside.Input.Demand, slot => Assert.Equal(1, slot.RequiredDrivers));
    }

    [Fact]
    public async Task MissingInsideRosterReturnsAnUnsavedManualDraftWithoutWritingData()
    {
        using var db = NewDb();
        AddEmployee(db, [RoleNames.Manager]);
        AddEmployee(db, [RoleNames.InStore]);
        AddDemand(db, DemandKinds.Inside, 2);
        await db.SaveChangesAsync();

        var draft = await new RosterPlanService(db, Inputs(db)).GetInsideDraftAsync(Monday, default);

        Assert.Equal(Guid.Empty, draft.Id);
        Assert.Equal(RosterKinds.Inside, draft.RosterKind);
        Assert.Equal("draft", draft.SolverStatus);
        Assert.Equal(2, draft.Employees.Count);
        Assert.All(draft.Coverage, slot => Assert.Equal(2, slot.Required));
        Assert.False(await db.RosterPlans.AnyAsync());
    }

    [Fact]
    public void ManualRosterRequestValidationAcceptsInsideButGenerationRemainsDriverOnly()
    {
        var request = new Roaster_Generator.Contracts.Roster.RosterPlanUpdateRequest
        {
            RosterKind = RosterKinds.Inside,
            WeekStart = Monday
        };

        Assert.True(new RosterPlanUpdateRequestValidator().Validate(request).IsValid);
        Assert.True(RosterKinds.IsGenerationEnabled(RosterKinds.Drivers));
        Assert.False(RosterKinds.IsGenerationEnabled(RosterKinds.Inside));
    }

    [Fact]
    public async Task MissingInsideDemandReturnsAValidationResponseInsteadOfAServerError()
    {
        using var db = NewDb();
        var inputs = Inputs(db);
        var controller = new AdminRosterController(
            new WeekSelectionRequestValidator(), new RosterPlanUpdateRequestValidator(),
            null!, null!, null!, new RosterPlanService(db, inputs), null!, null!);

        var response = await controller.Get(null, Monday, default, RosterKinds.Inside);

        Assert.IsType<BadRequestObjectResult>(response);
    }

    private static AppDbContext NewDb()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Database.EnsureCreated();
        return db;
    }

    private static RosterInputService Inputs(AppDbContext db) =>
        new(db, new RosterSettingsService(db), Options.Create(new ShopHoursOptions()));

    private static Employee AddEmployee(AppDbContext db, string[] roles)
    {
        var employee = new Employee
        {
            Id = Guid.NewGuid(), EmployeeNumber = Guid.NewGuid().ToString(), FirstName = "Test",
            LastName = roles.Contains(RoleNames.Manager) ? "Manager" : roles.Contains(RoleNames.InStore) ? "InStore" : "Driver",
            PhoneNumber = "000", IsActive = true,
            DriverProfile = roles.Contains(RoleNames.Driver) ? new DriverProfile() : null,
            ManagerProfile = roles.Contains(RoleNames.Manager) ? new ManagerProfile() : null,
            InStoreProfile = roles.Contains(RoleNames.InStore) ? new InStoreProfile() : null
        };
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), EmployeeId = employee.Id, Employee = employee,
            UserName = employee.EmployeeNumber
        };
        employee.User = user;
        db.Employees.Add(employee);
        db.Users.Add(user);
        foreach (var roleName in roles)
        {
            var role = db.Roles.Local.SingleOrDefault(item => item.Name == roleName);
            if (role is null)
            {
                role = new IdentityRole<Guid>(roleName)
                {
                    Id = Guid.NewGuid(), NormalizedName = roleName.ToUpperInvariant()
                };
                db.Roles.Add(role);
            }
            db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = role.Id });
        }
        db.Shifts.Add(new Shift
        {
            Id = Guid.NewGuid(), EmployeeId = employee.Id, Employee = employee, Date = Monday,
            StartTime = new TimeOnly(12, 0), FinishTime = new TimeOnly(1, 0)
        });
        return employee;
    }

    private static void AddDemand(AppDbContext db, string demandKind, int required)
    {
        var plan = new DemandPlan
        {
            Id = Guid.NewGuid(), Name = demandKind, WeekStart = Monday, DemandKind = demandKind
        };
        plan.Columns = Enumerable.Range(0, 7).Select(position => new DemandColumn
        {
            Id = Guid.NewGuid(), DemandPlanId = plan.Id, DemandPlan = plan, Position = position,
            Label = Monday.AddDays(position).DayOfWeek.ToString(), TargetSales = demandKind == DemandKinds.Inside ? 2000 : 1000
        }).ToList();
        plan.Rows = Enumerable.Range(12, 13).Select(hour =>
        {
            var row = new DemandRow { Id = Guid.NewGuid(), DemandPlanId = plan.Id, DemandPlan = plan, Hour = hour % 24 };
            row.Values = plan.Columns.Select(column => new DemandValue
            {
                Id = Guid.NewGuid(), DemandRowId = row.Id, Row = row,
                DemandColumnId = column.Id, Column = column,
                Demand = demandKind == DemandKinds.Outside ? required : null,
                InsideDemand = demandKind == DemandKinds.Inside ? required : null,
                Pizzas = demandKind == DemandKinds.Inside ? 40 : null
            }).ToList();
            return row;
        }).ToList();
        db.DemandPlans.Add(plan);
    }
}
