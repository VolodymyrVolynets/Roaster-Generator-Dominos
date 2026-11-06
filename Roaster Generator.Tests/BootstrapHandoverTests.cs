using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Roaster_Generator.Configuration;
using Roaster_Generator.Contracts.Employees;
using Roaster_Generator.Controllers;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Security;
using Roaster_Generator.Services;
using Roaster_Generator.Validation;

namespace Roaster_Generator.Tests;

public sealed class BootstrapHandoverTests
{
    [Fact]
    public async Task RestartDoesNotRecreateBootstrapOrPromoteAnEmployeeReusingItsUsername()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        services.Configure<AuthOptions>(options =>
        {
            options.AdminUsername = "bootstrap-login";
            options.AdminPassword = Guid.NewGuid().ToString("N");
        });
        services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.Password.RequiredLength = 5;
            options.Password.RequireDigit = false;
            options.Password.RequireLowercase = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
        }).AddRoles<IdentityRole<Guid>>().AddEntityFrameworkStores<AppDbContext>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        await IdentitySeeder.SeedAsync(scope.ServiceProvider);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var bootstrap = await users.FindByNameAsync("bootstrap-login");
        Assert.NotNull(bootstrap);
        var controller = new AdminEmployeesController(db, users, new EmployeeRequestValidator())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
            { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, RoleNames.Admin)], "Test")) } }
        };
        Assert.IsType<OkObjectResult>(await controller.Create(new EmployeeRequest
        {
            EmployeeNumber = "employee-admin", FirstName = "Employee", LastName = "Admin", PhoneNumber = "",
            Roles = [RoleNames.Manager, RoleNames.Admin]
        }, default));
        Assert.True(await users.IsLockedOutAsync(bootstrap));
        Assert.Null(await users.FindByNameAsync("bootstrap-login"));
        Assert.IsType<OkObjectResult>(await controller.Create(new EmployeeRequest
        {
            EmployeeNumber = "bootstrap-login", FirstName = "Ordinary", LastName = "Employee", PhoneNumber = "",
            Roles = [RoleNames.Driver]
        }, default));
        db.ChangeTracker.Clear();
        await IdentitySeeder.SeedAsync(scope.ServiceProvider);
        var employeeUser = await users.FindByNameAsync("bootstrap-login");
        Assert.NotNull(employeeUser);
        Assert.NotNull(employeeUser.EmployeeId);
        Assert.False(await users.IsInRoleAsync(employeeUser, RoleNames.Admin));
        Assert.True(await users.IsInRoleAsync(employeeUser, RoleNames.Driver));
        var retired = await db.Users.SingleAsync(user => user.Id == bootstrap.Id);
        Assert.True(await users.IsLockedOutAsync(retired));
        Assert.Single(await db.Users.Where(user => user.EmployeeId == null).ToListAsync());

        var config = scope.ServiceProvider.GetRequiredService<IOptions<AuthOptions>>().Value;
        config.AdminUsername = "different-bootstrap-login";
        db.ChangeTracker.Clear();
        await IdentitySeeder.SeedAsync(scope.ServiceProvider);
        Assert.Null(await users.FindByNameAsync(config.AdminUsername));

        var removedEmployeeId = employeeUser.EmployeeId!.Value;
        Assert.IsType<NoContentResult>(await controller.Delete(removedEmployeeId, default));
        db.ChangeTracker.Clear();
        await IdentitySeeder.SeedAsync(scope.ServiceProvider);
        Assert.Null(await users.FindByNameAsync("bootstrap-login"));
        Assert.True((await db.Employees.SingleAsync(employee => employee.Id == removedEmployeeId)).IsDeleted);
    }
}
