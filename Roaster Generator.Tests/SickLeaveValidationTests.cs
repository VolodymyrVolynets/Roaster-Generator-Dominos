using FluentValidation.TestHelper;
using Microsoft.AspNetCore.Http;
using Roaster_Generator.Contracts.SickLeave;
using Roaster_Generator.Services;
using Roaster_Generator.Validation;

namespace Roaster_Generator.Tests;

public sealed class SickLeaveValidationTests
{
    private readonly SickLeaveRequestValidator _validator = new();

    [Fact]
    public void RejectsFinishDateBeforeStartDate()
    {
        var request = ValidRequest();
        request.StartDate = new DateOnly(2026, 9, 11);
        request.FinishDate = new DateOnly(2026, 9, 10);

        _validator.TestValidate(request).ShouldHaveValidationErrorFor(item => item.FinishDate);
    }

    [Fact]
    public void RequiresSickNote()
    {
        var request = ValidRequest();
        request.File = null;

        _validator.TestValidate(request).ShouldHaveValidationErrorFor(item => item.File);
    }

    [Fact]
    public async Task AcceptsPdfWithMatchingSignature()
    {
        var content = "%PDF-1.7\nmedical note"u8.ToArray();
        var file = FormFile(content, "note.pdf");

        var result = await SickNoteFileValidator.ReadAsync(file, CancellationToken.None);

        Assert.Equal("application/pdf", result.ContentType);
        Assert.Equal("note.pdf", result.FileName);
        Assert.Equal(content, result.Content);
    }

    [Fact]
    public async Task RejectsFileWhoseSignatureDoesNotMatchExtension()
    {
        var file = FormFile("not an image"u8.ToArray(), "note.png");

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            SickNoteFileValidator.ReadAsync(file, CancellationToken.None));

        Assert.Contains("do not match", exception.Message);
    }

    private static SickLeaveCreateRequest ValidRequest() => new()
    {
        StartDate = new DateOnly(2026, 9, 11),
        FinishDate = new DateOnly(2026, 9, 12),
        File = FormFile("%PDF-1.7"u8.ToArray(), "note.pdf")
    };

    private static FormFile FormFile(byte[] content, string fileName)
    {
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, content.Length, "file", fileName);
    }
}
