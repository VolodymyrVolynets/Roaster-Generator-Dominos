namespace Roaster_Generator.Contracts.Settings;

public sealed class StoreSettingsRequest
{
    public string StoreId { get; set; } = string.Empty;

    public string StoreName { get; set; } = string.Empty;
}

public sealed class StoreSettingsResponse
{
    public string StoreId { get; init; } = string.Empty;

    public string StoreName { get; init; } = string.Empty;
}
