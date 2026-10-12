using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Roaster_Generator.Controllers;
using Roaster_Generator.Services;
using Roaster_Generator.Security;
using Roaster_Generator.Configuration;
using System.Security.Claims;
using Roaster_Generator.Contracts.Auth;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Validation;

namespace Roaster_Generator.Tests;

public sealed class InitialPasswordTests
{
    [Theory]
    [InlineData(RoleNames.Admin)]
    [InlineData(RoleNames.Manager)]
    [InlineData(RoleNames.Driver)]
    [InlineData(RoleNames.InStore)]
    public async Task InitialPasswordChangeUpdatesFlagAndPreservesIdentityForEveryRole(string role)
    {
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddDataProtection().UseEphemeralDataProtectionProvider();
        collection.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        collection.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        collection.AddIdentityCore<ApplicationUser>(options =>
        {
            options.Password.RequiredLength = 5;
            options.Password.RequireDigit = false;
            options.Password.RequireLowercase = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
        }).AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>().AddSignInManager().AddDefaultTokenProviders();
        collection.AddSignalR();
        collection.AddScoped<ApplicationEventPublisher>();
        using var provider = collection.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        Assert.True((await roles.CreateAsync(new IdentityRole<Guid>(role))).Succeeded);
        var user = new ApplicationUser { UserName = "first-login", EmailConfirmed = true };
        // Test-only generated credentials never appear in assertions or output.
        var initial = Guid.NewGuid().ToString("N") + "aA1!";
        var replacement = Guid.NewGuid().ToString("N") + "bB2!";
        Assert.True((await users.CreateAsync(user, initial)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Role, role)], "TestAuthentication")) };
        var signIn = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
        signIn.Context = context;
        var controller = new AuthController(users, signIn,
            scope.ServiceProvider.GetRequiredService<AppDbContext>(), NullLogger<AuthController>.Instance)
            { ControllerContext = new ControllerContext { HttpContext = context } };
        var validator = new ChangeInitialPasswordRequestValidator();
        var events = scope.ServiceProvider.GetRequiredService<ApplicationEventPublisher>();
        Assert.IsType<BadRequestObjectResult>(await controller.ChangeInitialPassword(
            new(initial, initial), validator, events, default));
        Assert.False(user.HasChangedPassword);
        Assert.IsType<OkResult>(await controller.ChangeInitialPassword(
            new(replacement, replacement), validator, events, default));
        Assert.True(user.HasChangedPassword);
        Assert.False(await users.CheckPasswordAsync(user, initial));
        Assert.True(await users.CheckPasswordAsync(user, replacement));
        Assert.True(await users.IsInRoleAsync(user, role));
        Assert.IsType<ConflictObjectResult>(await controller.ChangeInitialPassword(
            new(initial, initial), validator, events, default));
        var response = Assert.IsType<CurrentUserResponse>(Assert.IsType<OkObjectResult>(await controller.Me(default)).Value);
        Assert.True(response.HasChangedPassword);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var employee = new Employee { EmployeeNumber = "reset-test", FirstName = "Reset", LastName = "Test", IsActive = true };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        user.EmployeeId = employee.Id;
        Assert.True((await users.UpdateAsync(user)).Succeeded);
        var adminController = new AdminEmployeesController(db, users, new EmployeeRequestValidator(), events)
            { ControllerContext = new ControllerContext { HttpContext = context } };
        var stampBeforeReset = user.SecurityStamp;
        var reset = await adminController.ResetPassword(employee.Id, default);
        if (role != RoleNames.Admin)
        {
            Assert.IsType<ForbidResult>(reset);
            Assert.True(user.HasChangedPassword);
            Assert.Equal(stampBeforeReset, user.SecurityStamp);
        }
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Role, RoleNames.Admin)], "TestAuthentication"));
            reset = await adminController.ResetPassword(employee.Id, default);
            Assert.IsType<OkObjectResult>(reset);
            Assert.False(user.HasChangedPassword);
            Assert.NotEqual(stampBeforeReset, user.SecurityStamp);
            Assert.True(await users.CheckPasswordAsync(user, AuthOptions.DefaultEmployeePassword));
            Assert.False(await users.CheckPasswordAsync(user, replacement));
            Assert.True(await users.IsInRoleAsync(user, role));
            Assert.True(employee.IsActive);
            Assert.IsType<NotFoundObjectResult>(await adminController.ResetPassword(Guid.NewGuid(), default));
            Assert.IsType<OkResult>(await controller.ChangeInitialPassword(new(replacement, replacement), validator, events, default));
            Assert.True(user.HasChangedPassword);
        }
    }

    [Theory]
    [InlineData("", "", false)]
    [InlineData("1234", "1234", false)]
    [InlineData("chosen-password", "different-password", false)]
    [InlineData("chosen-password", "chosen-password", true)]
    public void PasswordAndConfirmationAreValidated(string password, string confirmation, bool valid) =>
        Assert.Equal(valid, new ChangeInitialPasswordRequestValidator().Validate(
            new ChangeInitialPasswordRequest(password, confirmation)).IsValid);

    [Fact]
    public void PasswordLengthIsBounded() =>
        Assert.False(new ChangeInitialPasswordRequestValidator().Validate(
            new ChangeInitialPasswordRequest(new string('a', 129), new string('a', 129))).IsValid);

    [Fact]
    public async Task PasswordChangeStatusPersistsWithoutChangingOtherAccounts()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var first = new ApplicationUser { Id = Guid.NewGuid() };
        var second = new ApplicationUser { Id = Guid.NewGuid() };
        db.Users.AddRange(first, second);
        await db.SaveChangesAsync();
        first.HasChangedPassword = true;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.True((await db.Users.SingleAsync(user => user.Id == first.Id)).HasChangedPassword);
        Assert.False((await db.Users.SingleAsync(user => user.Id == second.Id)).HasChangedPassword);
    }
}
