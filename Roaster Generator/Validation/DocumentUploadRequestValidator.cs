using FluentValidation;
using Roaster_Generator.Contracts.Documents;
using Roaster_Generator.Services;

namespace Roaster_Generator.Validation;

public sealed class DocumentUploadRequestValidator : AbstractValidator<DocumentAnalyseForm>
{
    public DocumentUploadRequestValidator()
    {
        RuleFor(request => request.Files)
            .NotEmpty().WithMessage("Choose a file to upload.")
            .Must(files => files.Count <= DocumentFileValidator.MaxFiles)
            .WithMessage($"Upload at most {DocumentFileValidator.MaxFiles} files.")
            .Must(files => files.All(file => file.Length > 0)).WithMessage("An uploaded file is empty.")
            .Must(files => files.Sum(file => file.Length) <= DocumentFileValidator.MaxTotalSize)
            .WithMessage("The files must be 20 MB or smaller in total.");
    }
}
