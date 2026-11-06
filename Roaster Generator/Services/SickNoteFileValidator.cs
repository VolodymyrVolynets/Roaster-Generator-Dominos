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
        var detectedType = FileSignature.DetectContentType(content);
        if (!string.Equals(expectedType, detectedType, StringComparison.Ordinal))
            throw new InvalidDataException("The sick note contents do not match its file extension.");

        return new ValidatedSickNote(content, originalName, detectedType!);
    }
}
