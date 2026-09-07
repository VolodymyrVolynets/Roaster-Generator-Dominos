using Microsoft.EntityFrameworkCore;
using Roaster_Generator.Contracts.Roster;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;

namespace Roaster_Generator.Services;

public sealed record RosterGenerationWeights(
    int TargetHoursWeight,
    int LongShiftBonus,
    int ShortShiftPenalty,
    int LateFinishPenalty,
    int EarlyStartPenalty);

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
        EarlyStartPenalty = 1
    };

    public async Task<RosterGenerationWeights> GetWeightsAsync(CancellationToken cancellationToken)
    {
        var settings = await db.RosterGenerationSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == RosterGenerationSettings.SingletonId, cancellationToken)
            ?? DefaultSettings;

        return ToWeights(settings);
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
    }

    private static RosterGenerationWeights ToWeights(RosterGenerationSettings settings) => new(
        settings.TargetHoursWeight,
        settings.LongShiftBonus,
        settings.ShortShiftPenalty,
        settings.LateFinishPenalty,
        settings.EarlyStartPenalty);

    private static RosterGenerationSettingsResponse ToResponse(RosterGenerationSettings settings) => new()
    {
        TargetHoursWeight = settings.TargetHoursWeight,
        LongShiftBonus = settings.LongShiftBonus,
        ShortShiftPenalty = settings.ShortShiftPenalty,
        LateFinishPenalty = settings.LateFinishPenalty,
        EarlyStartPenalty = settings.EarlyStartPenalty
    };
}
