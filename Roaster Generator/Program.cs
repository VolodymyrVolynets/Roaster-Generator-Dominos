using Npgsql;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    message = "Roaster Generator API",
    status = "ok"
}));

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

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
