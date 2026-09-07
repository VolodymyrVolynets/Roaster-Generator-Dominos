namespace Roaster_Generator.Entities;

public sealed class RosterGenerationSettings
{
    public static readonly Guid SingletonId =
        Guid.Parse("8f0a9c55-9d8a-4c1c-9a30-1d7e83e8c0f5");

    public Guid Id { get; set; } = SingletonId;

    public int TargetHoursWeight { get; set; } = 100;

    public int LongShiftBonus { get; set; } = 25;

    public int ShortShiftPenalty { get; set; } = 10;

    public int LateFinishPenalty { get; set; } = 2;

    public int EarlyStartPenalty { get; set; } = 1;
}
