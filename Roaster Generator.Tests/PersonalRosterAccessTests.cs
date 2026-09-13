using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Roaster_Generator.Contracts.Roster;
using Roaster_Generator.Controllers;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Security;
using Roaster_Generator.Services;
using Roaster_Generator.Validation;

namespace Roaster_Generator.Tests;

public sealed class PersonalRosterAccessTests
{
    [Fact]
    public async Task DriverReceivesOnlyTheirOwnPublishedShifts()
    {
        using var fixture = new PersonalRosterFixture();
        var monday = WeeklyScheduleService.GetWeekMonday(1);
        var result = Read(await fixture.Controller.Get(1, default));

        Assert.True(result.HasPublishedRoster);
        Assert.Equal(monday, result.WeekStart);
        Assert.Equal(monday.AddDays(6), result.WeekEnd);
        Assert.Equal(14, result.ScheduledHours);
        Assert.Equal(2, result.Shifts.Count);
        Assert.All(result.Shifts, shift => Assert.Contains(shift.Date, new[] { monday, monday.AddDays(2) }));
        Assert.DoesNotContain(result.Shifts, shift => shift.Date == monday.AddDays(1));
    }

    [Fact]
    public async Task MissingRosterReturnsAPrivateEmptyWeekInsteadOfManagementData()
    {
        using var fixture = new PersonalRosterFixture(addRoster: false);
        var result = Read(await fixture.Controller.Get(2, default));

        Assert.False(result.HasPublishedRoster);
        Assert.Empty(result.Shifts);
        Assert.Equal(0, result.ScheduledHours);
        Assert.Equal(WeeklyScheduleService.GetWeekMonday(2), result.WeekStart);
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("unlinked")]
    [InlineData("missing-user")]
    public async Task InvalidEmployeeStateCannotReadPersonalRoster(string scenario)
    {
        using var fixture = new PersonalRosterFixture();
        if (scenario == "inactive")
            (await fixture.Db.Employees.SingleAsync(employee => employee.Id == fixture.EmployeeId)).IsActive = false;
        else if (scenario == "unlinked")
            fixture.User.EmployeeId = null;
        else
            fixture.Db.Users.Remove(fixture.User);
        await fixture.Db.SaveChangesAsync();

        Assert.IsType<ForbidResult>(await fixture.Controller.Get(1, default));
    }

    [Fact]
    public async Task PersonalRosterValidatesTheRequestedWeek()
    {
        using var fixture = new PersonalRosterFixture();
        Assert.IsType<BadRequestObjectResult>(await fixture.Controller.Get(0, default));
    }

    [Fact]
    public void PersonalRosterRequiresTheDriverRole()
    {
        var authorize = Assert.Single(typeof(PersonalRosterController)
            .GetCustomAttributes(true).OfType<AuthorizeAttribute>());
        Assert.Equal(RoleNames.Driver, authorize.Roles);
        Assert.DoesNotContain(typeof(PersonalRosterController).GetCustomAttributes(true),
            attribute => attribute is AllowAnonymousAttribute);
    }

    private static PersonalRosterResponse Read(IActionResult result) =>
        Assert.IsType<PersonalRosterResponse>(Assert.IsType<OkObjectResult>(result).Value);

    private sealed class PersonalRosterFixture : IDisposable
    {
        private readonly ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        private readonly UserManager<ApplicationUser> userManager;

        public AppDbContext Db { get; } = new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public Guid EmployeeId { get; } = Guid.NewGuid();
        public ApplicationUser User { get; }
        public PersonalRosterController Controller { get; }

        public PersonalRosterFixture(bool addRoster = true)
        {
            var otherEmployeeId = Guid.NewGuid();
            Db.Employees.AddRange(
                new Employee { Id = EmployeeId, IsActive = true },
                new Employee { Id = otherEmployeeId, IsActive = true });
            User = new ApplicationUser
            {
                Id = Guid.NewGuid(), UserName = "driver-roster-test", EmployeeId = EmployeeId
            };
            Db.Users.Add(User);

            if (addRoster)
            {
                var monday = WeeklyScheduleService.GetWeekMonday(1);
                var plan = new RosterPlan
                {
                    Id = Guid.NewGuid(), WeekStart = monday, RosterKind = RosterKinds.Drivers,
                    CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow
                };
                Db.RosterPlans.Add(plan);
                Db.RosterShifts.AddRange(
                    Shift(plan.Id, EmployeeId, monday, 10, 18),
                    Shift(plan.Id, otherEmployeeId, monday.AddDays(1), 12, 18),
                    Shift(plan.Id, EmployeeId, monday.AddDays(2), 18, 0));
            }
            Db.SaveChanges();

            userManager = new UserManager<ApplicationUser>(
                new UserStore<ApplicationUser, IdentityRole<Guid>, AppDbContext, Guid>(Db),
                Options.Create(new IdentityOptions()), new PasswordHasher<ApplicationUser>(), [], [],
                new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(), services,
                NullLogger<UserManager<ApplicationUser>>.Instance);
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, User.Id.ToString()),
                new Claim(ClaimTypes.Role, RoleNames.Driver)
            };
            Controller = new PersonalRosterController(Db, userManager, new WeekSelectionRequestValidator())
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuthentication"))
                    }
                }
            };
        }

        private static RosterShift Shift(Guid planId, Guid employeeId, DateOnly date, int start, int finish) => new()
        {
            Id = Guid.NewGuid(), RosterPlanId = planId, EmployeeId = employeeId, Date = date,
            StartTime = new TimeOnly(start, 0), FinishTime = new TimeOnly(finish, 0)
        };

        public void Dispose()
        {
            userManager.Dispose();
            Db.Dispose();
            services.Dispose();
        }
    }
}
