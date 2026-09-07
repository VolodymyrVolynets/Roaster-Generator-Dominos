namespace Roaster_Generator.Contracts.Auth;

public sealed record LoginRequest(string Username, string Password);

public sealed class CurrentUserResponse
{
    public string Username { get; init; } = string.Empty;

    public bool IsAdmin { get; init; }

    public Guid? EmployeeId { get; init; }

    public string? EmployeeName { get; init; }
}
