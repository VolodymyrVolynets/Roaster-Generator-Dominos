using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Roaster_Generator.Configuration;

namespace Roaster_Generator.Services;

public sealed record ProtectedDocumentFile(byte[] Ciphertext, byte[] Nonce, byte[] Tag, string KeyId);

/// <summary>
/// Encrypts document files with AES-256-GCM. The file id is authenticated with the content,
/// so ciphertext copied onto another row fails to decrypt.
/// </summary>
public sealed class DocumentFileProtector(IOptions<DocumentStorageOptions> options)
{
    private const int KeySize = 32;

    private readonly Lazy<byte[]> key = new(() => ParseKey(options.Value.EncryptionKey));

    public bool IsConfigured
    {
        get
        {
            try
            {
                _ = key.Value;
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    public ProtectedDocumentFile Protect(Guid fileId, ReadOnlySpan<byte> content)
    {
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];
        var ciphertext = new byte[content.Length];
        using var aes = new AesGcm(key.Value, tag.Length);
        aes.Encrypt(nonce, content, ciphertext, tag, fileId.ToByteArray());
        return new(ciphertext, nonce, tag, options.Value.EncryptionKeyId);
    }

    /// <exception cref="CryptographicException">The file was altered, moved to another row, or the key is wrong.</exception>
    public byte[] Unprotect(Guid fileId, ProtectedDocumentFile file)
    {
        if (!string.Equals(file.KeyId, options.Value.EncryptionKeyId, StringComparison.Ordinal))
            throw new CryptographicException("The document was encrypted with a different key.");

        var content = new byte[file.Ciphertext.Length];
        using var aes = new AesGcm(key.Value, file.Tag.Length);
        aes.Decrypt(file.Nonce, file.Ciphertext, file.Tag, content, fileId.ToByteArray());
        return content;
    }

    private static byte[] ParseKey(string encoded)
    {
        byte[] parsed;
        try
        {
            parsed = Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Documents:EncryptionKey is not valid base64.");
        }

        return parsed.Length == KeySize
            ? parsed
            : throw new InvalidOperationException("Documents:EncryptionKey must be a base64 encoded 32-byte key.");
    }
}
