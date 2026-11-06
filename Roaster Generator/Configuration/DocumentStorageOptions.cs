namespace Roaster_Generator.Configuration;

public sealed class DocumentStorageOptions
{
    public const string SectionName = "Documents";

    /// <summary>Base64 encoded 32-byte key. Losing it makes every stored document unreadable.</summary>
    public string EncryptionKey { get; set; } = string.Empty;

    /// <summary>Recorded with each file so a key can be rotated later.</summary>
    public string EncryptionKeyId { get; set; } = "v1";
}
