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
using Roaster_Generator.Configuration;
using Roaster_Generator.Contracts.Schedules;
using Roaster_Generator.Controllers;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Security;
using Roaster_Generator.Services;
using Roaster_Generator.Validation;

namespace Roaster_Generator.Tests;

public sealed class EmployeeAvailabilityAccessTests
{
    [Theory]
    [InlineData(RoleNames.Driver)]
    [InlineData(RoleNames.InStore)]
    [InlineData(RoleNames.Manager)]
    [InlineData(RoleNames.Driver, RoleNames.Manager)]
    public async Task ActiveEmployeesCanReadAndReplaceTheirOwnAvailability(params string[] roles)
    {
        using var fixture = new ScheduleFixture(roles);
        var controller = fixture.Controller;

        var original = ReadSchedule(await controller.GetSchedule(fixture.EmployeeId, 1, default));
        Assert.Equal("12:00", original.Days[0].StartTime);

        var saved = ReadSchedule(await controller.SaveSchedule(fixture.EmployeeId, NewAvailability(), default));
        Assert.Equal("14:00", saved.Days[0].StartTime);
        Assert.Null(saved.Days[1].StartTime);

        var reloaded = ReadSchedule(await controller.GetSchedule(fixture.EmployeeId, 1, default));
        Assert.Equal("20:00", reloaded.Days[0].FinishTime);
        Assert.Single(await fixture.Db.Shifts.Where(shift => shift.EmployeeId == fixture.EmployeeId).ToListAsync());
        Assert.Equal(new TimeOnly(12, 0), (await fixture.Db.Shifts.SingleAsync(shift => shift.EmployeeId == fixture.OtherEmployeeId)).StartTime);
    }

    [Theory]
    [InlineData(RoleNames.Driver, "other-employee")]
    [InlineData(RoleNames.InStore, "other-employee")]
    [InlineData(RoleNames.Manager, "other-employee")]
    [InlineData(RoleNames.Driver, "inactive")]
    [InlineData(RoleNames.InStore, "inactive")]
    [InlineData(RoleNames.Manager, "inactive")]
    [InlineData(RoleNames.Driver, "unlinked")]
    [InlineData(RoleNames.InStore, "unlinked")]
    [InlineData(RoleNames.Manager, "unlinked")]
    [InlineData(RoleNames.Driver, "missing-user")]
    [InlineData(RoleNames.InStore, "missing-user")]
    [InlineData(RoleNames.Manager, "missing-user")]
    [InlineData(RoleNames.Manager, "missing-employee")]
    public async Task InvalidOwnershipOrEmployeeStatePreventsReadingAndWriting(string role, string scenario)
    {
        using var fixture = new ScheduleFixture([role]);
        var requestedEmployeeId = fixture.EmployeeId;
        switch (scenario)
        {
            case "other-employee":
                requestedEmployeeId = fixture.OtherEmployeeId;
                break;
            case "inactive":
                (await fixture.Db.Employees.SingleAsync(employee => employee.Id == fixture.EmployeeId)).IsActive = false;
                break;
            case "unlinked":
                fixture.User.EmployeeId = null;
                fixture.User.Employee = null;
                break;
            case "missing-user":
                fixture.Db.Users.Remove(fixture.User);
                break;
            case "missing-employee":
                requestedEmployeeId = Guid.NewGuid();
                fixture.User.EmployeeId = requestedEmployeeId;
                fixture.User.Employee = null;
                break;
        }
        await fixture.Db.SaveChangesAsync();

        Assert.IsType<ForbidResult>(await fixture.Controller.GetSchedule(requestedEmployeeId, 1, default));
        Assert.IsType<ForbidResult>(await fixture.Controller.SaveSchedule(requestedEmployeeId, NewAvailability(), default));
        Assert.Equal(3, await fixture.Db.Shifts.CountAsync());
        Assert.All(await fixture.Db.Shifts.ToListAsync(), shift => Assert.Equal(new TimeOnly(12, 0), shift.StartTime));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AdministratorsRetainOtherEmployeeScheduleAccess(bool employeeIsActive)
    {
        using var fixture = new ScheduleFixture([RoleNames.Admin], linkUser: false);
        (await fixture.Db.Employees.SingleAsync(employee => employee.Id == fixture.OtherEmployeeId)).IsActive = employeeIsActive;
        await fixture.Db.SaveChangesAsync();

        var schedule = ReadSchedule(await fixture.Controller.GetSchedule(fixture.OtherEmployeeId, 1, default));
        Assert.Equal(fixture.OtherEmployeeId, schedule.EmployeeId);

        var saved = ReadSchedule(await fixture.Controller.SaveSchedule(fixture.OtherEmployeeId, NewAvailability(), default));
        Assert.Equal("14:00", saved.Days[0].StartTime);
        Assert.Equal(new TimeOnly(14, 0), (await fixture.Db.Shifts.SingleAsync(shift => shift.EmployeeId == fixture.OtherEmployeeId)).StartTime);
    }

    [Fact]
    public async Task EmployeesCannotEditNextWeekFromSaturdayButCanEditTheFollowingWeek()
    {
        // 23:30 UTC on Friday is 00:30 Saturday in Dublin during daylight saving time.
        using var fixture = new ScheduleFixture([RoleNames.Driver],
            now: new DateTimeOffset(2026, 9, 11, 23, 30, 0, TimeSpan.Zero));

        var lockedWeek = ReadSchedule(await fixture.Controller.GetSchedule(fixture.EmployeeId, 1, default));
        Assert.False(lockedWeek.CanEdit);
        Assert.Equal(2, lockedWeek.MinimumEditableWeekOffset);

        var lockedResult = Assert.IsType<BadRequestObjectResult>(
            await fixture.Controller.SaveSchedule(fixture.EmployeeId, NewAvailability(), default));
        var problem = Assert.IsType<ValidationProblemDetails>(lockedResult.Value);
        Assert.Contains(nameof(WeeklyScheduleRequest.WeekOffset), problem.Errors.Keys);
        Assert.Contains(AvailabilityEditPolicy.WeekendLockMessage,
            problem.Errors[nameof(WeeklyScheduleRequest.WeekOffset)]);

        var saved = ReadSchedule(await fixture.Controller.SaveSchedule(
            fixture.EmployeeId, NewAvailability(2), default));
        Assert.True(saved.CanEdit);
        Assert.Equal(2, saved.MinimumEditableWeekOffset);
        Assert.Equal(WeeklyScheduleService.GetWeekMonday(2), saved.WeekStart);
    }

    [Fact]
    public async Task AdministratorsCanEditNextWeekOnSaturday()
    {
        using var fixture = new ScheduleFixture([RoleNames.Admin], linkUser: false,
            now: new DateTimeOffset(2026, 9, 11, 23, 30, 0, TimeSpan.Zero));

        var schedule = ReadSchedule(await fixture.Controller.GetSchedule(fixture.OtherEmployeeId, 1, default));
        Assert.True(schedule.CanEdit);
        Assert.Equal(1, schedule.MinimumEditableWeekOffset);

        var saved = ReadSchedule(await fixture.Controller.SaveSchedule(
            fixture.OtherEmployeeId, NewAvailability(), default));
        Assert.True(saved.CanEdit);
    }

    [Fact]
    public async Task ManagerAvailabilityStillValidatesTheEditableWeekAndShopHours()
    {
        using var fixture = new ScheduleFixture([RoleNames.Manager]);
        Assert.IsType<BadRequestObjectResult>(await fixture.Controller.GetSchedule(fixture.EmployeeId, 0, default));

        var request = NewAvailability();
        request.Days[0].StartTime = new TimeOnly(8, 0);
        var result = Assert.IsType<BadRequestObjectResult>(await fixture.Controller.SaveSchedule(fixture.EmployeeId, request, default));
        var problem = Assert.IsType<ValidationProblemDetails>(result.Value);
        Assert.Contains("Days[0].StartTime", problem.Errors.Keys);
        Assert.Equal(3, await fixture.Db.Shifts.CountAsync());
    }

    [Fact]
    public void EmployeeScheduleEndpointsRequireAuthentication()
    {
        var controller = typeof(EmployeesController);
        Assert.Contains(controller.GetCustomAttributes(true), attribute => attribute is AuthorizeAttribute);
        Assert.DoesNotContain(controller.GetCustomAttributes(true), attribute => attribute is AllowAnonymousAttribute);
        foreach (var method in new[] { nameof(EmployeesController.GetSchedule), nameof(EmployeesController.SaveSchedule) })
            Assert.DoesNotContain(controller.GetMethod(method)!.GetCustomAttributes(true), attribute => attribute is AllowAnonymousAttribute);
    }

    private static WeeklyScheduleResponse ReadSchedule(IActionResult result) =>
        Assert.IsType<WeeklyScheduleResponse>(Assert.IsType<OkObjectResult>(result).Value);

    private static WeeklyScheduleRequest NewAvailability(int weekOffset = 1) => new()
    {
        WeekOffset = weekOffset,
        Days = Enumerable.Range(0, 7).Select(day => new ScheduleDayRequest
        {
            Date = WeeklyScheduleService.GetWeekMonday(weekOffset).AddDays(day),
            StartTime = day == 0 ? new TimeOnly(14, 0) : null,
            FinishTime = day == 0 ? new TimeOnly(20, 0) : null
        }).ToList()
    };

    private sealed class ScheduleFixture : IDisposable
    {
        private readonly ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        private readonly UserManager<ApplicationUser> userManager;

        public AppDbContext Db { get; } = new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public Guid EmployeeId { get; } = Guid.NewGuid();
        public Guid OtherEmployeeId { get; } = Guid.NewGuid();
        public ApplicationUser User { get; }
        public EmployeesController Controller { get; }

        public ScheduleFixture(string[] roles, bool linkUser = true, DateTimeOffset? now = null)
        {
            Db.Employees.AddRange(new Employee { Id = EmployeeId }, new Employee { Id = OtherEmployeeId });
            var monday = WeeklyScheduleService.GetWeekMonday(1);
            foreach (var (employeeId, date) in new[] { (EmployeeId, monday), (EmployeeId, monday.AddDays(1)), (OtherEmployeeId, monday) })
                Db.Shifts.Add(new Shift
                {
                    Id = Guid.NewGuid(), EmployeeId = employeeId, Date = date,
                    StartTime = new TimeOnly(12, 0), FinishTime = new TimeOnly(18, 0)
                });
            User = new ApplicationUser { Id = Guid.NewGuid(), UserName = "availability-test", EmployeeId = linkUser ? EmployeeId : null };
            Db.Users.Add(User);
            Db.SaveChanges();

            userManager = new UserManager<ApplicationUser>(
                new UserStore<ApplicationUser, IdentityRole<Guid>, AppDbContext, Guid>(Db),
                Options.Create(new IdentityOptions()), new PasswordHasher<ApplicationUser>(), [], [],
                new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(), services,
                NullLogger<UserManager<ApplicationUser>>.Instance);
            var claims = roles.Select(role => new Claim(ClaimTypes.Role, role)).ToList();
            claims.Add(new Claim(ClaimTypes.NameIdentifier, User.Id.ToString()));
            Controller = new EmployeesController(Db, userManager, new WeekSelectionRequestValidator(),
                new WeeklyScheduleRequestValidator(Options.Create(new ShopHoursOptions())), new WeeklyScheduleService(Db),
                new AvailabilityEditPolicy(new FixedTimeProvider(now ?? new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero))))
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

        public void Dispose()
        {
            userManager.Dispose();
            Db.Dispose();
            services.Dispose();
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
