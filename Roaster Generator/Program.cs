using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Roaster_Generator.Configuration;
using Roaster_Generator.Contracts.Auth;
using Roaster_Generator.Contracts.Employees;
using Roaster_Generator.Contracts.Schedules;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;
using Roaster_Generator.Security;
using Roaster_Generator.Services;
using Roaster_Generator.Validation;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:Postgres is not configured.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));
builder.Services.AddOptions<AuthOptions>()
    .Bind(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.AddOptions<ShopHoursOptions>()
    .Bind(builder.Configuration.GetSection(ShopHoursOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = false;
        options.Password.RequiredLength = 5;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager();
builder.Services
    .AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();
builder.Services.AddAuthorization();
builder.Services.AddScoped<WeeklyScheduleService>();
builder.Services.AddScoped<IValidator<WeeklyScheduleRequest>, WeeklyScheduleRequestValidator>();
builder.Services.AddScoped<IValidator<WeekSelectionRequest>, WeekSelectionRequestValidator>();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await IdentitySeeder.SeedAsync(scope.ServiceProvider);
}

app.MapGet("/", () => Results.Ok(new
{
    message = "Roaster Generator API",
    status = "ok"
}));

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/auth/login", async Task<IResult> (
    LoginRequest request,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    AppDbContext db,
    CancellationToken cancellationToken) =>
{
    var user = await userManager.FindByNameAsync(request.Username);

    if (user is null)
    {
        return Results.Unauthorized();
    }

    if (user.EmployeeId is Guid employeeId &&
        !await db.Employees.AnyAsync(employee => employee.Id == employeeId && employee.IsActive, cancellationToken))
    {
        return Results.Unauthorized();
    }

    var passwordResult = await signInManager.CheckPasswordSignInAsync(
        user,
        request.Password,
        lockoutOnFailure: false);

    if (!passwordResult.Succeeded)
    {
        return Results.Unauthorized();
    }

    await signInManager.SignInAsync(user, isPersistent: true);

    return Results.Ok();
});

app.MapGet("/api/auth/me", async Task<IResult> (
    ClaimsPrincipal principal,
    UserManager<ApplicationUser> userManager,
    AppDbContext db,
    CancellationToken cancellationToken) =>
{
    var user = await userManager.GetUserAsync(principal);

    if (user is null)
    {
        return Results.Unauthorized();
    }

    Employee? employee = null;

    if (user.EmployeeId is Guid employeeId)
    {
        employee = await db.Employees
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == employeeId, cancellationToken);

        if (employee is null || !employee.IsActive)
        {
            return Results.Unauthorized();
        }
    }

    var roles = await userManager.GetRolesAsync(user);

    return Results.Ok(new CurrentUserResponse
    {
        Username = user.UserName ?? string.Empty,
        IsAdmin = roles.Contains(RoleNames.Admin),
        EmployeeId = user.EmployeeId,
        EmployeeName = employee is null
            ? null
            : $"{employee.FirstName} {employee.LastName}".Trim()
    });
}).RequireAuthorization();

app.MapPost("/api/auth/logout", async (SignInManager<ApplicationUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return Results.NoContent();
}).RequireAuthorization();

app.MapGet("/api/employees", async (
    AppDbContext db,
    ClaimsPrincipal principal,
    UserManager<ApplicationUser> userManager,
    CancellationToken cancellationToken) =>
{
    var query = db.Employees.AsNoTracking();

    if (!principal.IsInRole(RoleNames.Admin))
    {
        var user = await userManager.GetUserAsync(principal);

        if (user?.EmployeeId is not Guid employeeId)
        {
            return Results.Unauthorized();
        }

        query = query.Where(employee => employee.Id == employeeId && employee.IsActive);
    }

    var employees = (await query
        .OrderBy(employee => employee.LastName)
        .ThenBy(employee => employee.FirstName)
        .ToListAsync(cancellationToken))
        .Select(ToEmployeeResponse)
        .ToList();

    return Results.Ok(employees);
}).RequireAuthorization();

app.MapGet("/api/employees/{employeeId:guid}/schedule", async Task<IResult> (
    Guid employeeId,
    int? weekOffset,
    IValidator<WeekSelectionRequest> weekValidator,
    WeeklyScheduleService schedules,
    ClaimsPrincipal principal,
    UserManager<ApplicationUser> userManager,
    AppDbContext db,
    CancellationToken cancellationToken) =>
{
    if (!principal.IsInRole(RoleNames.Admin))
    {
        var user = await userManager.GetUserAsync(principal);

        if (user?.EmployeeId != employeeId ||
            !await db.Employees.AnyAsync(employee => employee.Id == employeeId && employee.IsActive, cancellationToken))
        {
            return Results.Forbid();
        }
    }

    var selection = new WeekSelectionRequest
    {
        WeekOffset = weekOffset ?? WeeklyScheduleService.MinWeekOffset
    };
    var validationResult = await weekValidator.ValidateAsync(selection, cancellationToken);

    if (!validationResult.IsValid)
    {
        var errors = validationResult.Errors
            .GroupBy(error => error.PropertyName)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.ErrorMessage).ToArray());

        return Results.ValidationProblem(
            errors,
            title: "The selected week is invalid.");
    }

    var schedule = await schedules.GetWeekAsync(
        employeeId,
        selection.WeekOffset,
        cancellationToken);

    return schedule is null
        ? Results.NotFound(new { message = "Employee not found." })
        : Results.Ok(schedule);
}).RequireAuthorization();

app.MapPut("/api/employees/{employeeId:guid}/schedule", async Task<IResult> (
    Guid employeeId,
    WeeklyScheduleRequest request,
    IValidator<WeeklyScheduleRequest> validator,
    WeeklyScheduleService schedules,
    ClaimsPrincipal principal,
    UserManager<ApplicationUser> userManager,
    AppDbContext db,
    CancellationToken cancellationToken) =>
{
    if (!principal.IsInRole(RoleNames.Admin))
    {
        var user = await userManager.GetUserAsync(principal);

        if (user?.EmployeeId != employeeId ||
            !await db.Employees.AnyAsync(employee => employee.Id == employeeId && employee.IsActive, cancellationToken))
        {
            return Results.Forbid();
        }
    }

    var validationResult = await validator.ValidateAsync(request, cancellationToken);

    if (!validationResult.IsValid)
    {
        var errors = validationResult.Errors
            .GroupBy(error => error.PropertyName)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.ErrorMessage).ToArray());

        return Results.ValidationProblem(
            errors,
            title: "The shift schedule is invalid.");
    }

    if (employeeId == Guid.Empty)
    {
        return Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["employeeId"] = ["Employee ID must not be empty."]
            },
            title: "The shift schedule is invalid.");
    }

    var schedule = await schedules.ReplaceWeekAsync(
        employeeId,
        request,
        cancellationToken);

    return schedule is null
        ? Results.NotFound(new { message = "Employee not found." })
        : Results.Ok(schedule);
}).RequireAuthorization();

app.MapGet("/api/admin/employees", async (
    AppDbContext db,
    CancellationToken cancellationToken) =>
{
    var employees = (await db.Employees
        .AsNoTracking()
        .OrderBy(employee => employee.LastName)
        .ThenBy(employee => employee.FirstName)
        .ToListAsync(cancellationToken))
        .Select(ToEmployeeResponse)
        .ToList();

    return Results.Ok(employees);
}).RequireAuthorization(policy => policy.RequireRole(RoleNames.Admin));

app.MapPost("/api/admin/employees", async Task<IResult> (
    EmployeeRequest request,
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    CancellationToken cancellationToken) =>
{
    var employeeNumber = request.EmployeeNumber.Trim();
    var firstName = request.FirstName.Trim();
    var lastName = request.LastName.Trim();
    var phoneNumber = request.PhoneNumber.Trim();

    if (string.IsNullOrWhiteSpace(employeeNumber) ||
        string.IsNullOrWhiteSpace(firstName) ||
        string.IsNullOrWhiteSpace(lastName))
    {
        return Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["employee"] = ["Employee number, first name, and last name are required."]
            },
            title: "The employee is invalid.");
    }

    if (await db.Employees.AnyAsync(employee => employee.EmployeeNumber == employeeNumber, cancellationToken) ||
        await userManager.FindByNameAsync(employeeNumber) is not null)
    {
        return Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["employeeNumber"] = ["Employee number is already in use."]
            },
            title: "The employee is invalid.");
    }

    await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

    var employee = new Employee
    {
        EmployeeNumber = employeeNumber,
        FirstName = firstName,
        LastName = lastName,
        PhoneNumber = phoneNumber,
        IsActive = true
    };

    db.Employees.Add(employee);
    await db.SaveChangesAsync(cancellationToken);

    var user = new ApplicationUser
    {
        UserName = employeeNumber,
        Email = $"employee-{employeeNumber}@local.invalid",
        EmailConfirmed = true,
        EmployeeId = employee.Id
    };
    var userResult = await userManager.CreateAsync(user, AuthOptions.DefaultEmployeePassword);

    if (!userResult.Succeeded)
    {
        return Results.ValidationProblem(
            userResult.Errors
                .GroupBy(error => "user")
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(error => error.Description).ToArray()),
            title: "The employee user could not be created.");
    }

    var roleResult = await userManager.AddToRoleAsync(user, RoleNames.User);

    if (!roleResult.Succeeded)
    {
        return Results.ValidationProblem(
            roleResult.Errors
                .GroupBy(error => "role")
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(error => error.Description).ToArray()),
            title: "The employee role could not be assigned.");
    }

    await transaction.CommitAsync(cancellationToken);
    return Results.Ok(ToEmployeeResponse(employee));
}).RequireAuthorization(policy => policy.RequireRole(RoleNames.Admin));

app.MapPut("/api/admin/employees/{employeeId:guid}", async Task<IResult> (
    Guid employeeId,
    EmployeeRequest request,
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    CancellationToken cancellationToken) =>
{
    var employee = await db.Employees.SingleOrDefaultAsync(
        item => item.Id == employeeId,
        cancellationToken);

    if (employee is null)
    {
        return Results.NotFound(new { message = "Employee not found." });
    }

    var employeeNumber = request.EmployeeNumber.Trim();
    var firstName = request.FirstName.Trim();
    var lastName = request.LastName.Trim();
    var phoneNumber = request.PhoneNumber.Trim();

    if (string.IsNullOrWhiteSpace(employeeNumber) ||
        string.IsNullOrWhiteSpace(firstName) ||
        string.IsNullOrWhiteSpace(lastName))
    {
        return Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["employee"] = ["Employee number, first name, and last name are required."]
            },
            title: "The employee is invalid.");
    }

    if (await db.Employees.AnyAsync(
            item => item.Id != employeeId && item.EmployeeNumber == employeeNumber,
            cancellationToken))
    {
        return Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["employeeNumber"] = ["Employee number is already in use."]
            },
            title: "The employee is invalid.");
    }

    var user = await userManager.Users
        .SingleOrDefaultAsync(item => item.EmployeeId == employeeId, cancellationToken);

    if (user is not null && !string.Equals(user.UserName, employeeNumber, StringComparison.Ordinal))
    {
        var userResult = await userManager.SetUserNameAsync(user, employeeNumber);

        if (!userResult.Succeeded)
        {
            return Results.ValidationProblem(
                userResult.Errors
                    .GroupBy(error => "user")
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(error => error.Description).ToArray()),
                title: "The employee login could not be updated.");
        }
    }

    employee.EmployeeNumber = employeeNumber;
    employee.FirstName = firstName;
    employee.LastName = lastName;
    employee.PhoneNumber = phoneNumber;
    await db.SaveChangesAsync(cancellationToken);

    return Results.Ok(ToEmployeeResponse(employee));
}).RequireAuthorization(policy => policy.RequireRole(RoleNames.Admin));

app.MapPost("/api/admin/employees/{employeeId:guid}/deactivate", async Task<IResult> (
    Guid employeeId,
    AppDbContext db,
    CancellationToken cancellationToken) =>
{
    var employee = await db.Employees.SingleOrDefaultAsync(
        item => item.Id == employeeId,
        cancellationToken);

    if (employee is null)
    {
        return Results.NotFound(new { message = "Employee not found." });
    }

    employee.IsActive = false;
    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(ToEmployeeResponse(employee));
}).RequireAuthorization(policy => policy.RequireRole(RoleNames.Admin));

app.MapPost("/api/admin/employees/{employeeId:guid}/reactivate", async Task<IResult> (
    Guid employeeId,
    AppDbContext db,
    CancellationToken cancellationToken) =>
{
    var employee = await db.Employees.SingleOrDefaultAsync(
        item => item.Id == employeeId,
        cancellationToken);

    if (employee is null)
    {
        return Results.NotFound(new { message = "Employee not found." });
    }

    employee.IsActive = true;
    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(ToEmployeeResponse(employee));
}).RequireAuthorization(policy => policy.RequireRole(RoleNames.Admin));

app.MapGet("/api/admin/availability", async Task<IResult> (
    int? weekOffset,
    IValidator<WeekSelectionRequest> weekValidator,
    WeeklyScheduleService schedules,
    CancellationToken cancellationToken) =>
{
    var selection = new WeekSelectionRequest
    {
        WeekOffset = weekOffset ?? WeeklyScheduleService.MinWeekOffset
    };
    var validationResult = await weekValidator.ValidateAsync(selection, cancellationToken);

    if (!validationResult.IsValid)
    {
        var errors = validationResult.Errors
            .GroupBy(error => error.PropertyName)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.ErrorMessage).ToArray());

        return Results.ValidationProblem(
            errors,
            title: "The selected week is invalid.");
    }

    return Results.Ok(await schedules.GetWeekForAllAsync(
        selection.WeekOffset,
        cancellationToken));
}).RequireAuthorization(policy => policy.RequireRole(RoleNames.Admin));

app.MapGet("/api/hello", async (
    IConfiguration configuration,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var connectionString = configuration.GetConnectionString("Postgres");

    if (string.IsNullOrWhiteSpace(connectionString))
    {
        return Results.Problem(
            title: "PostgreSQL is not configured",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    try
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand("SELECT CURRENT_TIMESTAMP", connection);
        var databaseTime = await command.ExecuteScalarAsync(cancellationToken);

        return Results.Ok(new
        {
            message = "Hello World from the API",
            database = "connected",
            databaseTime
        });
    }
    catch (Exception exception) when (exception is NpgsqlException or TimeoutException)
    {
        logger.LogError(exception, "Unable to connect to PostgreSQL.");

        return Results.Problem(
            title: "PostgreSQL is unavailable",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

static EmployeeResponse ToEmployeeResponse(Employee employee) => new()
{
    Id = employee.Id,
    EmployeeNumber = employee.EmployeeNumber,
    FirstName = employee.FirstName,
    LastName = employee.LastName,
    PhoneNumber = employee.PhoneNumber,
    IsActive = employee.IsActive
};

app.Run();
