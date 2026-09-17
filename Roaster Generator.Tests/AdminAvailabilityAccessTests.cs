using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Roaster_Generator.Controllers;
using Roaster_Generator.Contracts.Schedules;
using Roaster_Generator.Data;
using Roaster_Generator.Security;
using Roaster_Generator.Services;
using Roaster_Generator.Validation;

namespace Roaster_Generator.Tests;

public sealed class AdminAvailabilityAccessTests
{
    [Theory]
    [InlineData(RoleNames.Admin, 4)]
    [InlineData(RoleNames.Admin, -4)]
    [InlineData(RoleNames.Manager, 4)]
    [InlineData(RoleNames.Manager, -4)]
    public async Task ManagementCanViewAvailabilityOutsideTheThreeWeekHorizon(string role, int weekOffset)
    {
        using var db = NewDb();
        var controller = CreateController(db, role);

        var result = Assert.IsType<OkObjectResult>(
            await controller.Get(weekOffset, default));
        var response = Assert.IsType<WeeklyAvailabilityResponse>(result.Value);

        Assert.Equal(WeeklyScheduleService.GetWeekMonday(weekOffset), response.WeekStart);
    }

    private static AdminAvailabilityController CreateController(AppDbContext db, string role)
    {
        var claims = new[] { new Claim(ClaimTypes.Role, role) };
        return new AdminAvailabilityController(
            new WeekSelectionRequestValidator(),
            new AdminWeekSelectionRequestValidator(),
            new WeeklyScheduleService(db, null))
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

    private static AppDbContext NewDb()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Database.EnsureCreated();
        return db;
    }
}
