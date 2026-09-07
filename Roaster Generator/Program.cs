using Microsoft.EntityFrameworkCore;
using Npgsql;
using Roaster_Generator.Contracts.Schedules;
using Roaster_Generator.Data;
using Roaster_Generator.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:Postgres is not configured.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));
builder.Services.AddScoped<WeeklyScheduleService>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.MapGet("/", () => Results.Ok(new
{
    message = "Roaster Generator API",
    status = "ok"
}));

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/api/employees", async (
    AppDbContext db,
    CancellationToken cancellationToken) =>
{
    var employees = await db.Employees
        .AsNoTracking()
        .OrderBy(employee => employee.LastName)
        .ThenBy(employee => employee.FirstName)
        .ToListAsync(cancellationToken);

    return Results.Ok(employees);
});

app.MapGet("/api/employees/{employeeId:guid}/schedule/next-week", async Task<IResult> (
    Guid employeeId,
    WeeklyScheduleService schedules,
    CancellationToken cancellationToken) =>
{
    var schedule = await schedules.GetNextWeekAsync(employeeId, cancellationToken);

    return schedule is null
        ? Results.NotFound(new { message = "Employee not found." })
        : Results.Ok(schedule);
});

app.MapPut("/api/employees/{employeeId:guid}/schedule/next-week", async Task<IResult> (
    Guid employeeId,
    WeeklyScheduleRequest request,
    WeeklyScheduleService schedules,
    CancellationToken cancellationToken) =>
{
    try
    {
        var schedule = await schedules.ReplaceNextWeekAsync(
            employeeId,
            request,
            cancellationToken);

        return schedule is null
            ? Results.NotFound(new { message = "Employee not found." })
            : Results.Ok(schedule);
    }
    catch (ScheduleValidationException exception)
    {
        return Results.BadRequest(new { message = exception.Message });
    }
});

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

app.Run();
