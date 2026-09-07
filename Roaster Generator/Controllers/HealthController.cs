using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace Roaster_Generator.Controllers;

[ApiController]
public sealed class HealthController(
    IConfiguration configuration,
    ILogger<HealthController> logger) : ControllerBase
{
    [HttpGet("/")]
    public IActionResult Root() => Ok(new
    {
        message = "Roaster Generator API",
        status = "ok"
    });

    [HttpGet("/health")]
    public IActionResult Health() => Ok(new { status = "ok" });

    [HttpGet("/api/hello")]
    public async Task<IActionResult> Hello(CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("Postgres");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Problem(
                title: "PostgreSQL is not configured",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand("SELECT CURRENT_TIMESTAMP", connection);
            var databaseTime = await command.ExecuteScalarAsync(cancellationToken);

            return Ok(new
            {
                message = "Hello World from the API",
                database = "connected",
                databaseTime
            });
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException)
        {
            logger.LogError(exception, "Unable to connect to PostgreSQL.");

            return Problem(
                title: "PostgreSQL is unavailable",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
