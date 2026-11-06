namespace Roaster_Generator.Entities;

/// <summary>The manager's saved signature, placed automatically on forms that need one.</summary>
public sealed class DocumentManagerSignature
{
    public static readonly Guid SingletonId = new("7d9c6a3e-2f41-4b57-9a1e-3c5f0b8e6d12");

    public Guid Id { get; set; } = SingletonId;

    public string ManagerName { get; set; } = string.Empty;

    /// <summary>The employee whose own forms this signature must not sign.</summary>
    public Guid? ManagerEmployeeId { get; set; }

    public byte[] EncryptedContent { get; set; } = [];

    public byte[] Nonce { get; set; } = [];

    public byte[] Tag { get; set; } = [];

    public string KeyId { get; set; } = string.Empty;

    public DateTimeOffset SavedAtUtc { get; set; }
}
