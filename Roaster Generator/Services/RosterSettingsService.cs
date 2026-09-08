using Microsoft.EntityFrameworkCore;
using Roaster_Generator.Contracts.Roster;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;

namespace Roaster_Generator.Services;

public sealed class RosterSettingsService(AppDbContext db)
{
    public async Task<RosterSettingsRequest> GetAsync(CancellationToken ct) =>
        ToResponse(await db.RosterGenerationSettings.AsNoTracking()
            .SingleAsync(s => s.Id == RosterGenerationSettings.SingletonId, ct));

    public async Task<RosterSettingsRequest> SaveAsync(RosterSettingsRequest request, CancellationToken ct)
    {
        var settings = await db.RosterGenerationSettings
            .SingleAsync(s => s.Id == RosterGenerationSettings.SingletonId, ct);
        settings.TargetHoursWeight = request.TargetHoursWeight;
        settings.HistoryFairnessWeight = request.HistoryFairnessWeight;
        settings.HistoryShiftLengthWeight = request.HistoryShiftLengthWeight;
        settings.FairnessSpreadWeight = request.FairnessSpreadWeight;
        settings.LongShiftBonus = request.LongShiftBonus;
        settings.ShortShiftPenalty = request.ShortShiftPenalty;
        settings.DailyShiftCountPenalty = request.DailyShiftCountPenalty;
        settings.ShortBreakPenalty = request.ShortBreakPenalty;
        settings.MinimumRestHours = request.MinimumRestHours;
        settings.PreferredRestHours = request.PreferredRestHours;
        settings.LatestShiftStartHour = request.LatestShiftStartHour;
        settings.MaxSolveSeconds = request.MaxSolveSeconds;
        await db.SaveChangesAsync(ct);
        return ToResponse(settings);
    }

    public static RosterSettingsRequest ToResponse(RosterGenerationSettings settings) => new()
    {
        TargetHoursWeight = settings.TargetHoursWeight,
        HistoryFairnessWeight = settings.HistoryFairnessWeight,
        HistoryShiftLengthWeight = settings.HistoryShiftLengthWeight,
        FairnessSpreadWeight = settings.FairnessSpreadWeight,
        LongShiftBonus = settings.LongShiftBonus,
        ShortShiftPenalty = settings.ShortShiftPenalty,
        DailyShiftCountPenalty = settings.DailyShiftCountPenalty,
        ShortBreakPenalty = settings.ShortBreakPenalty,
        MinimumRestHours = settings.MinimumRestHours,
        PreferredRestHours = settings.PreferredRestHours,
        LatestShiftStartHour = settings.LatestShiftStartHour,
        MaxSolveSeconds = settings.MaxSolveSeconds
    };

    public static RosterSolverOptions ToOptions(RosterSettingsRequest settings) => new()
    {
        TargetHoursWeight = settings.TargetHoursWeight,
        HistoryFairnessWeight = settings.HistoryFairnessWeight,
        HistoryShiftLengthWeight = settings.HistoryShiftLengthWeight,
        FairnessSpreadWeight = settings.FairnessSpreadWeight,
        LongShiftBonus = settings.LongShiftBonus,
        ShortShiftPenalty = settings.ShortShiftPenalty,
        DailyShiftCountPenalty = settings.DailyShiftCountPenalty,
        ShortBreakPenalty = settings.ShortBreakPenalty,
        MinimumRestHours = settings.MinimumRestHours,
        PreferredRestHours = settings.PreferredRestHours,
        LatestShiftStartHour = settings.LatestShiftStartHour,
        MaxSolveSeconds = settings.MaxSolveSeconds
    };
}
