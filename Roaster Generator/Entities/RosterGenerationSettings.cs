namespace Roaster_Generator.Entities;

public sealed class RosterGenerationSettings
{
    public static readonly Guid SingletonId =
        Guid.Parse("8f0a9c55-9d8a-4c1c-9a30-1d7e83e8c0f5");

    public Guid Id { get; set; } = SingletonId;

    public int TargetHoursWeight { get; set; } = 100;

    public int HistoryFairnessWeight { get; set; } = 100;

    public int FairnessSpreadWeight { get; set; } = 1000;

    public int LongShiftBonus { get; set; } = 25;

    public int ShortShiftPenalty { get; set; } = 10;

    public int DailyShiftCountPenalty { get; set; } = 25;

    public int ShortBreakPenalty { get; set; } = 100;

    public int MinimumRestHours { get; set; } = 8;

    public int PreferredRestHours { get; set; } = 12;

    public int MaxSolveSeconds { get; set; } = 20;

    public int DailyOptionPoolSize { get; set; } = 256;

    public int DailyOptionSearchNodeLimit { get; set; } = 100_000;

    public int PopulationSize { get; set; } = 24;

    public int GenerationCount { get; set; } = 150;

    public decimal MutationRate { get; set; } = 0.03m;

    public int EliteCount { get; set; } = 2;

    public int TournamentSize { get; set; } = 2;

    public int ExactSearchNodeLimit { get; set; } = 500_000;
}
