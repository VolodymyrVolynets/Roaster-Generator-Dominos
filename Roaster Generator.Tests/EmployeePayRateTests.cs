using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Roaster_Generator.Contracts.Employees;
using Roaster_Generator.Controllers;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Security;
using Roaster_Generator.Validation;

namespace Roaster_Generator.Tests;

public sealed class EmployeePayRateTests
{
    [Theory]
    [InlineData("-0.01", false)]
    [InlineData("10000.01", false)]
    [InlineData("14.501", false)]
    [InlineData("0", true)]
    [InlineData("14.50", true)]
    [InlineData("10000", true)]
    [InlineData(null, true)]
    public void EmployeePayRequiresNonnegativeCurrencyWithinTheLimit(string? hourlyRate, bool expected)
    {
        var request = Request();
        request.HourlyRate = hourlyRate is null ? null : decimal.Parse(hourlyRate, System.Globalization.CultureInfo.InvariantCulture);
        var result = new EmployeeRequestValidator().Validate(request);

        Assert.Equal(expected, result.IsValid);
        if (!expected)
            Assert.All(result.Errors, error => Assert.Equal(nameof(request.HourlyRate), error.PropertyName));
    }

    [Theory]
    [InlineData(RoleNames.Driver)]
    [InlineData(RoleNames.InStore)]
    [InlineData(RoleNames.Manager)]
    public async Task NewlyCreatedEmployeesReceiveTheDefaultPayAcrossRoles(string role)
    {
        using var fixture = new EmployeeFixture();
        var request = Request(role);
        var response = ReadEmployee(await fixture.Controller.Create(request, default));

        fixture.Db.ChangeTracker.Clear();
        var saved = await fixture.Db.Employees.SingleAsync(employee => employee.Id == response.Id);
        Assert.Equal(14.5m, saved.HourlyRate);
        Assert.Equal(14.5m, response.HourlyRate);
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);
        Assert.Equal(14.5m, document.RootElement.GetProperty("hourlyRate").GetDecimal());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("18.75")]
    public async Task ExplicitPayPersistsThroughCreateReadAndUpdate(string amount)
    {
        using var fixture = new EmployeeFixture();
        var request = Request();
        request.HourlyRate = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture);
        var created = ReadEmployee(await fixture.Controller.Create(request, default));
        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(request.HourlyRate, (await fixture.Db.Employees.SingleAsync(employee => employee.Id == created.Id)).HourlyRate);

        request.HourlyRate = amount == "0" ? 18.75m : 0m;
        var updated = ReadEmployee(await fixture.Controller.Update(created.Id, request, default));
        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(request.HourlyRate, updated.HourlyRate);
        Assert.Equal(request.HourlyRate, (await fixture.Db.Employees.SingleAsync(employee => employee.Id == created.Id)).HourlyRate);
    }

    [Fact]
    public async Task LegacyEmployeeUpdatesPreserveExistingPayAndValidationErrorsDoNotChangeIt()
    {
        using var fixture = new EmployeeFixture();
        var request = Request();
        request.HourlyRate = 21.35m;
        var created = ReadEmployee(await fixture.Controller.Create(request, default));

        var legacy = JsonSerializer.Deserialize<EmployeeRequest>(
            "{\"employeeNumber\":\"pay-test\",\"firstName\":\"Updated\",\"lastName\":\"Employee\",\"roles\":[\"Driver\"]}",
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var updated = ReadEmployee(await fixture.Controller.Update(created.Id, legacy, default));
        Assert.Equal("Updated", updated.FirstName);
        Assert.Equal(21.35m, updated.HourlyRate);

        legacy.HourlyRate = -1m;
        var invalid = Assert.IsType<BadRequestObjectResult>(await fixture.Controller.Update(created.Id, legacy, default));
        var problem = Assert.IsType<ValidationProblemDetails>(invalid.Value);
        Assert.Contains(nameof(EmployeeRequest.HourlyRate), problem.Errors.Keys);
        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(21.35m, (await fixture.Db.Employees.SingleAsync(employee => employee.Id == created.Id)).HourlyRate);
    }

    [Fact]
    public void DatabaseMappingKeepsExplicitZeroPayAndSuppliesDefaultForExistingEmployees()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=unused").Options);
        var property = db.Model.FindEntityType(typeof(Employee))!.FindProperty(nameof(Employee.HourlyRate))!;

        Assert.Equal(14.5m, new Employee().HourlyRate);
        Assert.Equal(14.5m, property.GetDefaultValue());
        Assert.Equal(12, property.GetPrecision());
        Assert.Equal(2, property.GetScale());
        Assert.Equal(ValueGenerated.Never, property.ValueGenerated);
    }

    private static EmployeeRequest Request(string role = RoleNames.Driver) => new()
    {
        EmployeeNumber = "pay-test", FirstName = "Test", LastName = "Employee", Roles = [role]
    };

    [Theory]
    [InlineData(null, true)]
    [InlineData(0, true)]
    [InlineData(45, true)]
    [InlineData(168, true)]
    [InlineData(-1, false)]
    [InlineData(169, false)]
    public void MaximumWeeklyHoursRequiresAValidWholeHourLimit(int? maximum, bool expected)
    {
        var request = Request();
        request.MaximumWeeklyHours = maximum;
        var validation = new EmployeeRequestValidator().Validate(request);
        Assert.Equal(expected, validation.IsValid);
        if (!expected) Assert.All(validation.Errors,
            error => Assert.Equal(nameof(EmployeeRequest.MaximumWeeklyHours), error.PropertyName));
    }

    [Theory]
    [InlineData(RoleNames.Driver)]
    [InlineData(RoleNames.InStore)]
    [InlineData(RoleNames.Manager)]
    public async Task MaximumHoursDefaultPersistAndSurviveLegacyUpdatesAcrossRoles(string role)
    {
        using var fixture = new EmployeeFixture();
        var request = Request(role);
        var created = ReadEmployee(await fixture.Controller.Create(request, default));
        Assert.Equal(45, created.MaximumWeeklyHours);

        request.MaximumWeeklyHours = 0;
        var zero = ReadEmployee(await fixture.Controller.Update(created.Id, request, default));
        Assert.Equal(0, zero.MaximumWeeklyHours);
        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(0, (await fixture.Db.Employees.SingleAsync(e => e.Id == created.Id)).MaximumWeeklyHours);

        request.MaximumWeeklyHours = 60;
        Assert.Equal(60, ReadEmployee(await fixture.Controller.Update(created.Id, request, default)).MaximumWeeklyHours);
        request.MaximumWeeklyHours = null;
        Assert.Equal(60, ReadEmployee(await fixture.Controller.Update(created.Id, request, default)).MaximumWeeklyHours);
        request.MaximumWeeklyHours = -1;
        Assert.IsType<BadRequestObjectResult>(await fixture.Controller.Update(created.Id, request, default));
        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(60, (await fixture.Db.Employees.SingleAsync(e => e.Id == created.Id)).MaximumWeeklyHours);
    }

    [Fact]
    public void MaximumHoursDatabaseMappingPreservesExplicitZeroAndDefaultsToFortyFive()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=unused").Options);
        var property = db.Model.FindEntityType(typeof(Employee))!.FindProperty(nameof(Employee.MaximumWeeklyHours))!;
        Assert.Equal(45, new Employee().MaximumWeeklyHours);
        Assert.Equal(45, property.GetDefaultValue());
        Assert.Equal(ValueGenerated.Never, property.ValueGenerated);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(60)]
    public async Task ExplicitMaximumHoursPersistOnCreate(int maximum)
    {
        using var fixture = new EmployeeFixture();
        var request = Request();
        request.MaximumWeeklyHours = maximum;
        var created = ReadEmployee(await fixture.Controller.Create(request, default));
        Assert.Equal(maximum, created.MaximumWeeklyHours);
        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(maximum, (await fixture.Db.Employees.SingleAsync(e => e.Id == created.Id)).MaximumWeeklyHours);
    }

    private static EmployeeResponse ReadEmployee(IActionResult result) =>
        Assert.IsType<EmployeeResponse>(Assert.IsType<OkObjectResult>(result).Value);

    private sealed class EmployeeFixture : IDisposable
    {
        private readonly ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        private readonly UserManager<ApplicationUser> userManager;
        public AppDbContext Db { get; } = new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
        public AdminEmployeesController Controller { get; }

        public EmployeeFixture()
        {
            foreach (var role in new[] { RoleNames.Driver, RoleNames.InStore, RoleNames.Manager })
                Db.Roles.Add(new IdentityRole<Guid> { Id = Guid.NewGuid(), Name = role, NormalizedName = role.ToUpperInvariant() });
            Db.SaveChanges();
            userManager = new UserManager<ApplicationUser>(
                new UserStore<ApplicationUser, IdentityRole<Guid>, AppDbContext, Guid>(Db),
                Options.Create(new IdentityOptions()), new PasswordHasher<ApplicationUser>(), [], [],
                new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(), services,
                NullLogger<UserManager<ApplicationUser>>.Instance);
            Controller = new AdminEmployeesController(Db, userManager, new EmployeeRequestValidator())
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, RoleNames.Admin)], "TestAuthentication"))
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
}
