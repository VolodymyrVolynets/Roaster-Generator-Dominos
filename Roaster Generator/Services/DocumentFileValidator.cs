using Microsoft.AspNetCore.Http;
using PdfSharp.Pdf.IO;

namespace Roaster_Generator.Services;

public sealed record ValidatedDocumentFile(byte[] Content, string FileName, string ContentType, int PageCount = 1);

/// <summary>
/// Validates files against what storage and the AI analysis can take. The total stays at
/// 20 MB because base64 encoding has to fit inside the 32 MB Claude request limit.
/// </summary>
public static class DocumentFileValidator
{
    public const int MaxFiles = 4;
    public const int MaxPdfPages = 20;
    public const int MaxFileNameLength = 255;
    public const long MaxImageSize = 5 * 1024 * 1024;
    public const long MaxTotalSize = 20 * 1024 * 1024;

    private const string PdfContentType = "application/pdf";

    private static readonly Dictionary<string, string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = PdfContentType,
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp"
    };

    public static async Task<IReadOnlyList<ValidatedDocumentFile>> ReadAsync(
        IReadOnlyCollection<IFormFile> files, CancellationToken cancellationToken)
    {
        if (files.Count is 0 or > MaxFiles)
            throw new InvalidDataException($"Upload between 1 and {MaxFiles} files.");
        if (files.Any(file => file.Length <= 0))
            throw new InvalidDataException("An uploaded file is empty.");
        if (files.Sum(file => file.Length) > MaxTotalSize)
            throw new InvalidDataException("The files must be 20 MB or smaller in total.");

        var validated = new List<ValidatedDocumentFile>(files.Count);
        foreach (var file in files)
            validated.Add(await ReadFileAsync(file, cancellationToken));

        if (validated.Count > 1 && validated.Any(file => file.ContentType == PdfContentType))
            throw new InvalidDataException("Upload one PDF on its own, or up to 4 images.");
        return validated;
    }

    private static async Task<ValidatedDocumentFile> ReadFileAsync(IFormFile file, CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(file.FileName);
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > MaxFileNameLength ||
            !AllowedExtensions.TryGetValue(Path.GetExtension(fileName), out var expectedType))
            throw new InvalidDataException("Upload a PDF, JPEG, PNG, or WebP file.");

        await using var input = file.OpenReadStream();
        using var output = new MemoryStream((int)file.Length);
        await input.CopyToAsync(output, cancellationToken);
        var content = output.ToArray();

        if (!string.Equals(expectedType, FileSignature.DetectContentType(content), StringComparison.Ordinal))
            throw new InvalidDataException("The file contents do not match its file extension.");

        var pageCount = 1;
        if (expectedType == PdfContentType) pageCount = ReadablePdfPages(content);
        else if (content.Length > MaxImageSize) throw new InvalidDataException("Each image must be 5 MB or smaller.");

        return new(content, fileName, expectedType, pageCount);
    }

    private static int ReadablePdfPages(byte[] content)
    {
        // An encryption dictionary is outside compressed streams, and Claude cannot read encrypted PDFs
        // even when no password is needed to open them.
        if (content.AsSpan().IndexOf("/Encrypt"u8) >= 0)
            throw new InvalidDataException("Remove the password from this PDF and upload it again.");

        int pageCount;
        try
        {
            using var stream = new MemoryStream(content, writable: false);
            using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
            pageCount = document.PageCount;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidDataException("This PDF could not be read.");
        }

        if (pageCount is 0 or > MaxPdfPages)
            throw new InvalidDataException($"A PDF must have between 1 and {MaxPdfPages} pages.");
        return pageCount;
    }
}
