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

    [Theory]
    [InlineData(12, 22, true)]
    [InlineData(15, 1, true)]
    [InlineData(12, 23, false)]
    [InlineData(14, 1, false)]
    [InlineData(12, 1, false)]
    public async Task DriverDailyLimitCountsOvernightHours(int start, int finish, bool allowed)
    {
        using var fixture = new ScheduleFixture([RoleNames.Driver]);
        var current = ReadSchedule(await fixture.Controller.GetSchedule(fixture.EmployeeId, 1, default));
        Assert.Equal(10, current.MaximumAvailabilityHoursPerDay);
        Assert.Equal(6, current.MaximumAvailabilityDaysPerWeek);

        var result = await fixture.Controller.SaveSchedule(fixture.EmployeeId,
            NewAvailability(start: start, finish: finish), default);

        if (allowed)
        {
            var saved = ReadSchedule(result);
            Assert.Equal($"{start:00}:00", saved.Days[0].StartTime);
            Assert.Equal($"{finish:00}:00", saved.Days[0].FinishTime);
            Assert.Equal(10, saved.MaximumAvailabilityHoursPerDay);
            Assert.Equal(6, saved.MaximumAvailabilityDaysPerWeek);
        }
        else
        {
            var problem = Assert.IsType<ValidationProblemDetails>(Assert.IsType<BadRequestObjectResult>(result).Value);
            Assert.Contains(problem.Errors["Days[0].FinishTime"], message => message.Contains("10 hours"));
            Assert.Equal(3, await fixture.Db.Shifts.CountAsync());
            Assert.All(await fixture.Db.Shifts.ToListAsync(), shift => Assert.Equal(new TimeOnly(12, 0), shift.StartTime));
        }
    }

    [Theory]
    [InlineData(6, true)]
    [InlineData(7, false)]
    public async Task DriversCanSubmitSixDaysButNotSeven(int days, bool allowed)
    {
        using var fixture = new ScheduleFixture([RoleNames.Driver]);
        var result = await fixture.Controller.SaveSchedule(fixture.EmployeeId,
            NewAvailability(activeDays: days, start: 15, finish: 1), default);

        if (allowed)
        {
            var saved = ReadSchedule(result);
            Assert.Equal(6, saved.Days.Count(day => day.StartTime is not null));
            Assert.Equal(6, await fixture.Db.Shifts.CountAsync(shift => shift.EmployeeId == fixture.EmployeeId));
        }
        else
        {
            var problem = Assert.IsType<ValidationProblemDetails>(Assert.IsType<BadRequestObjectResult>(result).Value);
            Assert.Contains(problem.Errors["Days"], message => message.Contains("6 days"));
            Assert.Equal(3, await fixture.Db.Shifts.CountAsync());
        }
    }

    [Theory]
    [InlineData(RoleNames.Admin)]
    [InlineData(RoleNames.Admin, RoleNames.Driver)]
    public async Task AdminsCanOverrideBothDriverAvailabilityLimits(params string[] roles)
    {
        using var fixture = new ScheduleFixture(roles);
        var saved = ReadSchedule(await fixture.Controller.SaveSchedule(fixture.OtherEmployeeId,
            NewAvailability(activeDays: 7, start: 12, finish: 1), default));

        Assert.Null(saved.MaximumAvailabilityHoursPerDay);
        Assert.Null(saved.MaximumAvailabilityDaysPerWeek);
        Assert.All(saved.Days, day =>
        {
            Assert.Equal("12:00", day.StartTime);
            Assert.Equal("01:00", day.FinishTime);
        });
        Assert.Equal(7, await fixture.Db.Shifts.CountAsync(shift => shift.EmployeeId == fixture.OtherEmployeeId));
    }

    [Theory]
    [InlineData(RoleNames.InStore)]
    [InlineData(RoleNames.Manager)]
    public async Task DriverEntryLimitsDoNotChangeInsideEmployeeAvailability(string role)
    {
        using var fixture = new ScheduleFixture([role]);
        var saved = ReadSchedule(await fixture.Controller.SaveSchedule(fixture.EmployeeId,
            NewAvailability(activeDays: 7, start: 12, finish: 1), default));

        Assert.Equal(7, saved.Days.Count(day => day.StartTime is not null));
        Assert.Null(saved.MaximumAvailabilityHoursPerDay);
        Assert.Null(saved.MaximumAvailabilityDaysPerWeek);
    }

    [Fact]
    public async Task ManagerRoleDoesNotGrantADriverTheAdminOverride()
    {
        using var fixture = new ScheduleFixture([RoleNames.Driver, RoleNames.Manager]);
        var result = await fixture.Controller.SaveSchedule(fixture.EmployeeId,
            NewAvailability(activeDays: 7, start: 12, finish: 1), default);

        var problem = Assert.IsType<ValidationProblemDetails>(Assert.IsType<BadRequestObjectResult>(result).Value);
        Assert.Contains("Days", problem.Errors.Keys);
        Assert.Contains("Days[0].FinishTime", problem.Errors.Keys);
        Assert.Equal(3, await fixture.Db.Shifts.CountAsync());
    }

    [Theory]
    [InlineData(RoleNames.Admin)]
    [InlineData(RoleNames.Driver)]
    public async Task AvailabilityLimitsPreserveOrdinaryTimeValidation(string role)
    {
        using var fixture = new ScheduleFixture([role]);
        var request = NewAvailability();
        request.Days[0].StartTime = new TimeOnly(14, 30);
        var result = await fixture.Controller.SaveSchedule(fixture.EmployeeId, request, default);

        var problem = Assert.IsType<ValidationProblemDetails>(Assert.IsType<BadRequestObjectResult>(result).Value);
        Assert.Contains(problem.Errors["Days[0].StartTime"], message => message.Contains("whole hours"));
        Assert.Equal(3, await fixture.Db.Shifts.CountAsync());
    }

    [Fact]
    public async Task AdministratorsCanReadSchedulesOutsideTheThreeWeekEditHorizon()
    {
        using var fixture = new ScheduleFixture([RoleNames.Admin], linkUser: false);
        var weekStart = WeeklyScheduleService.GetWeekMonday(4);
        fixture.Db.Shifts.Add(new Shift
        {
            Id = Guid.NewGuid(), EmployeeId = fixture.OtherEmployeeId, Date = weekStart,
            StartTime = new TimeOnly(10, 0), FinishTime = new TimeOnly(18, 0)
        });
        await fixture.Db.SaveChangesAsync();

        var schedule = ReadSchedule(await fixture.Controller.GetSchedule(fixture.OtherEmployeeId, 4, default));

        Assert.Equal("10:00", schedule.Days[0].StartTime);
        Assert.False(schedule.CanEdit);
    }

    [Theory]
    [InlineData(-12)]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task AdministratorsCanEditEmployeeAvailabilityForPastAndCurrentWeeks(int weekOffset)
    {
        using var fixture = new ScheduleFixture([RoleNames.Admin], linkUser: false);

        var schedule = ReadSchedule(await fixture.Controller.GetSchedule(
            fixture.OtherEmployeeId, weekOffset, default));
        Assert.True(schedule.CanEdit);
        Assert.Null(schedule.MinimumEditableWeekOffset);

        var saved = ReadSchedule(await fixture.Controller.SaveSchedule(
            fixture.OtherEmployeeId, NewAvailability(weekOffset, start: 15, finish: 21), default));
        Assert.True(saved.CanEdit);
        Assert.Null(saved.MinimumEditableWeekOffset);
        Assert.Equal(WeeklyScheduleService.GetWeekMonday(weekOffset), saved.WeekStart);
        Assert.Equal("15:00", saved.Days[0].StartTime);
        Assert.Equal("21:00", saved.Days[0].FinishTime);
    }

    [Fact]
    public async Task AdministratorsCannotEditMoreThanThreeWeeksAhead()
    {
        using var fixture = new ScheduleFixture([RoleNames.Admin], linkUser: false);
        var schedule = ReadSchedule(await fixture.Controller.GetSchedule(fixture.OtherEmployeeId, 4, default));
        Assert.False(schedule.CanEdit);

        var result = Assert.IsType<BadRequestObjectResult>(await fixture.Controller.SaveSchedule(
            fixture.OtherEmployeeId, NewAvailability(4), default));
        var problem = Assert.IsType<ValidationProblemDetails>(result.Value);
        Assert.Contains(nameof(WeeklyScheduleRequest.WeekOffset), problem.Errors.Keys);
    }

    [Theory]
    [InlineData(RoleNames.Driver)]
    [InlineData(RoleNames.InStore)]
    [InlineData(RoleNames.Manager)]
    public async Task NonAdministratorsStillCannotEditPastAvailability(string role)
    {
        using var fixture = new ScheduleFixture([role]);
        var result = Assert.IsType<BadRequestObjectResult>(await fixture.Controller.SaveSchedule(
            fixture.EmployeeId, NewAvailability(-1), default));
        var problem = Assert.IsType<ValidationProblemDetails>(result.Value);
        Assert.Contains(nameof(WeeklyScheduleRequest.WeekOffset), problem.Errors.Keys);
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
        Assert.Null(schedule.MinimumEditableWeekOffset);

        var saved = ReadSchedule(await fixture.Controller.SaveSchedule(
            fixture.OtherEmployeeId, NewAvailability(), default));
        Assert.True(saved.CanEdit);
    }

    [Fact]
    public async Task ManagersCanReadHistoricalOwnAvailabilityButStillValidateEdits()
    {
        using var fixture = new ScheduleFixture([RoleNames.Manager]);
        var historical = ReadSchedule(await fixture.Controller.GetSchedule(fixture.EmployeeId, 0, default));
        Assert.False(historical.CanEdit);

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

    private static WeeklyScheduleRequest NewAvailability(int weekOffset = 1, int activeDays = 1, int start = 14, int finish = 20) => new()
    {
        WeekOffset = weekOffset,
        Days = Enumerable.Range(0, 7).Select(day => new ScheduleDayRequest
        {
            Date = WeeklyScheduleService.GetWeekMonday(weekOffset).AddDays(day),
            StartTime = day < activeDays ? new TimeOnly(start, 0) : null,
            FinishTime = day < activeDays ? new TimeOnly(finish, 0) : null
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
                new AdminWeekSelectionRequestValidator(), new AdminEditableWeekSelectionRequestValidator(),
                new WeeklyScheduleRequestValidator(Options.Create(new ShopHoursOptions())), new WeeklyScheduleService(Db, null),
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
