using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Roaster_Generator.Configuration;
using Roaster_Generator.Contracts.Settings;
using Roaster_Generator.Data;
using Roaster_Generator.Entities;

namespace Roaster_Generator.Services;

public sealed class StoreSettingsService(
    AppDbContext db,
    IOptions<AbsencePdfOptions> initialOptions)
{
    private readonly AbsencePdfOptions initialSettings = initialOptions.Value;

    public async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (await db.StoreSettings.AnyAsync(item => item.Id == StoreSettings.SingletonId, ct)) return;

        db.StoreSettings.Add(new StoreSettings
        {
            StoreId = initialSettings.StoreId.Trim(),
            StoreName = initialSettings.StoreName.Trim()
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<StoreSettingsResponse> GetAsync(CancellationToken ct)
    {
        var settings = await db.StoreSettings.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == StoreSettings.SingletonId, ct);
        return settings is null
            ? new StoreSettingsResponse
            {
                StoreId = initialSettings.StoreId.Trim(),
                StoreName = initialSettings.StoreName.Trim()
            }
            : ToResponse(settings);
    }

    public async Task<StoreSettingsResponse> SaveAsync(StoreSettingsRequest request, CancellationToken ct)
    {
        var settings = await db.StoreSettings
            .SingleOrDefaultAsync(item => item.Id == StoreSettings.SingletonId, ct);
        if (settings is null)
        {
            settings = new StoreSettings();
            db.StoreSettings.Add(settings);
        }

        settings.StoreId = request.StoreId.Trim();
        settings.StoreName = request.StoreName.Trim();
        await db.SaveChangesAsync(ct);
        return ToResponse(settings);
    }

    private static StoreSettingsResponse ToResponse(StoreSettings settings) => new()
    {
        StoreId = settings.StoreId,
        StoreName = settings.StoreName
    };
}
