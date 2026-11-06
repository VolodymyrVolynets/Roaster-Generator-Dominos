namespace Roaster_Generator.Entities;

/// <summary>
/// One encrypted file. It belongs to exactly one draft or document, so confirming a draft
/// moves the row without re-encrypting. <see cref="Id"/> is assigned before encryption
/// because the ciphertext is bound to it.
/// </summary>
public sealed class EmployeeDocumentFile
{
    public Guid Id { get; set; }

    public Guid? DocumentId { get; set; }

    public EmployeeDocument? Document { get; set; }

    public Guid? DraftId { get; set; }

    public DocumentDraft? Draft { get; set; }

    public int PageIndex { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long Size { get; set; }

    public byte[] EncryptedContent { get; set; } = [];

    public byte[] Nonce { get; set; } = [];

    public byte[] Tag { get; set; } = [];

    public string KeyId { get; set; } = string.Empty;
}
