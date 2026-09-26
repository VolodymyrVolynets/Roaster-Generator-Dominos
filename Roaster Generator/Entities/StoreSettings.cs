namespace Roaster_Generator.Entities;

public sealed class StoreSettings
{
    public static readonly Guid SingletonId =
        Guid.Parse("451dc6aa-36e7-4d36-bbfe-f4340bb6f5d1");

    public Guid Id { get; set; } = SingletonId;

    public string StoreId { get; set; } = string.Empty;

    public string StoreName { get; set; } = string.Empty;
}
