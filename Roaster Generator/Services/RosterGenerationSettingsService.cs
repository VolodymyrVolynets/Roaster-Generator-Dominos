using Microsoft.EntityFrameworkCore;
using Roaster_Generator.Contracts.Roster;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;

namespace Roaster_Generator.Services;

public sealed record RosterGenerationParameters(
    int TargetHoursWeight,
    int LongShiftBonus,
    int ShortShiftPenalty,
    int LateFinishPenalty,
    int EarlyStartPenalty,
    int PopulationSize,
    int GenerationCount,
    decimal MutationRate,
    int EliteCount,
    int TournamentSize,
    int ExactSearchNodeLimit);

public sealed class RosterGenerationSettingsValidationException(string message) : Exception(message);

public sealed class RosterGenerationSettingsService(AppDbContext db)
{
    private static RosterGenerationSettings DefaultSettings => new()
    {
        Id = RosterGenerationSettings.SingletonId,
        TargetHoursWeight = 100,
        LongShiftBonus = 25,
        ShortShiftPenalty = 10,
        LateFinishPenalty = 2,
        EarlyStartPenalty = 1,
        PopulationSize = 24,
        GenerationCount = 150,
        MutationRate = 0.03m,
        EliteCount = 2,
        TournamentSize = 2,
        ExactSearchNodeLimit = 500_000
    };

    public async Task<RosterGenerationParameters> GetParametersAsync(CancellationToken cancellationToken)
    {
        var settings = await db.RosterGenerationSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == RosterGenerationSettings.SingletonId, cancellationToken)
            ?? DefaultSettings;

        return ToParameters(settings);
    }

    public async Task<RosterGenerationSettingsResponse> GetAsync(CancellationToken cancellationToken)
    {
        var settings = await db.RosterGenerationSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == RosterGenerationSettings.SingletonId, cancellationToken)
            ?? DefaultSettings;

        return ToResponse(settings);
    }

    public async Task<RosterGenerationSettingsResponse> UpdateAsync(
        RosterGenerationSettingsRequest request,
        CancellationToken cancellationToken)
    {
        Validate(request);

        var settings = await db.RosterGenerationSettings
            .SingleOrDefaultAsync(item => item.Id == RosterGenerationSettings.SingletonId, cancellationToken);

        if (settings is null)
        {
            settings = DefaultSettings;
            db.RosterGenerationSettings.Add(settings);
        }

        settings.TargetHoursWeight = request.TargetHoursWeight;
        settings.LongShiftBonus = request.LongShiftBonus;
        settings.ShortShiftPenalty = request.ShortShiftPenalty;
        settings.LateFinishPenalty = request.LateFinishPenalty;
        settings.EarlyStartPenalty = request.EarlyStartPenalty;
        settings.PopulationSize = request.PopulationSize;
        settings.GenerationCount = request.GenerationCount;
        settings.MutationRate = request.MutationRate;
        settings.EliteCount = request.EliteCount;
        settings.TournamentSize = request.TournamentSize;
        settings.ExactSearchNodeLimit = request.ExactSearchNodeLimit;

        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(settings);
    }

    private static void Validate(RosterGenerationSettingsRequest request)
    {
        var invalid = new[]
        {
            (Name: "Target hours weight", Value: request.TargetHoursWeight),
            (Name: "Long shift bonus", Value: request.LongShiftBonus),
            (Name: "Short shift penalty", Value: request.ShortShiftPenalty),
            (Name: "Late finish penalty", Value: request.LateFinishPenalty),
            (Name: "Early start penalty", Value: request.EarlyStartPenalty)
        }.FirstOrDefault(item => item.Value is < 0 or > 1000);

        if (invalid != default)
        {
            throw new RosterGenerationSettingsValidationException(
                $"{invalid.Name} must be between 0 and 1000.");
        }

        if (request.PopulationSize is < 4 or > 200)
        {
            throw new RosterGenerationSettingsValidationException(
                "Population size must be between 4 and 200.");
        }

        if (request.GenerationCount is < 1 or > 5000)
        {
            throw new RosterGenerationSettingsValidationException(
                "Generation count must be between 1 and 5000.");
        }

        if (request.MutationRate is < 0 or > 1)
        {
            throw new RosterGenerationSettingsValidationException(
                "Mutation rate must be between 0 and 1.");
        }

        if (request.EliteCount is < 1 || request.EliteCount > request.PopulationSize)
        {
            throw new RosterGenerationSettingsValidationException(
                "Elite count must be at least 1 and no greater than population size.");
        }

        if (request.TournamentSize is < 2 || request.TournamentSize > request.PopulationSize)
        {
            throw new RosterGenerationSettingsValidationException(
                "Tournament size must be at least 2 and no greater than population size.");
        }

        if (request.ExactSearchNodeLimit is < 1_000 or > 5_000_000)
        {
            throw new RosterGenerationSettingsValidationException(
                "Exact-search node limit must be between 1000 and 5000000.");
        }
    }

    private static RosterGenerationParameters ToParameters(RosterGenerationSettings settings) => new(
        settings.TargetHoursWeight,
        settings.LongShiftBonus,
        settings.ShortShiftPenalty,
        settings.LateFinishPenalty,
        settings.EarlyStartPenalty,
        settings.PopulationSize,
        settings.GenerationCount,
        settings.MutationRate,
        settings.EliteCount,
        settings.TournamentSize,
        settings.ExactSearchNodeLimit);

    private static RosterGenerationSettingsResponse ToResponse(RosterGenerationSettings settings) => new()
    {
        TargetHoursWeight = settings.TargetHoursWeight,
        LongShiftBonus = settings.LongShiftBonus,
        ShortShiftPenalty = settings.ShortShiftPenalty,
        LateFinishPenalty = settings.LateFinishPenalty,
        EarlyStartPenalty = settings.EarlyStartPenalty,
        PopulationSize = settings.PopulationSize,
        GenerationCount = settings.GenerationCount,
        MutationRate = settings.MutationRate,
        EliteCount = settings.EliteCount,
        TournamentSize = settings.TournamentSize,
        ExactSearchNodeLimit = settings.ExactSearchNodeLimit
    };
}
