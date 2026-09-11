using Microsoft.AspNetCore.Http;

namespace Roaster_Generator.Services;

public sealed record ValidatedSickNote(byte[] Content, string FileName, string ContentType);

public static class SickNoteFileValidator
{
    public const long MaxFileSize = 10 * 1024 * 1024;

    private static readonly Dictionary<string, string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".bmp"] = "image/bmp"
    };

    public static async Task<ValidatedSickNote> ReadAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length is <= 0 or > MaxFileSize)
            throw new InvalidDataException("The sick note must be between 1 byte and 10 MB.");

        var originalName = Path.GetFileName(file.FileName);
        var extension = Path.GetExtension(originalName);
        if (string.IsNullOrWhiteSpace(originalName) || !AllowedExtensions.TryGetValue(extension, out var expectedType))
            throw new InvalidDataException("Upload a PDF, PNG, JPEG, GIF, WebP, or BMP sick note.");

        await using var input = file.OpenReadStream();
        using var output = new MemoryStream((int)file.Length);
        await input.CopyToAsync(output, cancellationToken);
        var content = output.ToArray();
        var detectedType = DetectContentType(content);
        if (!string.Equals(expectedType, detectedType, StringComparison.Ordinal))
            throw new InvalidDataException("The sick note contents do not match its file extension.");

        return new ValidatedSickNote(content, originalName, detectedType!);
    }

    private static string? DetectContentType(ReadOnlySpan<byte> content)
    {
        if (content.StartsWith("%PDF-"u8)) return "application/pdf";
        if (content.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "image/png";
        if (content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF) return "image/jpeg";
        if (content.StartsWith("GIF87a"u8) || content.StartsWith("GIF89a"u8)) return "image/gif";
        if (content.Length >= 12 && content[..4].SequenceEqual("RIFF"u8) && content.Slice(8, 4).SequenceEqual("WEBP"u8)) return "image/webp";
        if (content.StartsWith("BM"u8)) return "image/bmp";
        return null;
    }
}
