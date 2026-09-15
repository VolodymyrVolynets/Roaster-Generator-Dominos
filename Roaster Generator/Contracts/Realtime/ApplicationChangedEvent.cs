namespace Roaster_Generator.Contracts.Realtime;

public sealed class ApplicationChangedEvent
{
    public required string Type { get; init; }
    public Guid? EntityId { get; init; }
    public DateOnly? WeekStart { get; init; }
    public string? RosterKind { get; init; }
    public DateTimeOffset OccurredAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
