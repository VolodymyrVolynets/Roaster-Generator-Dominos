using FluentValidation;
using Roaster_Generator.Contracts.Documents;
using Roaster_Generator.Services;

namespace Roaster_Generator.Validation;

public sealed class DocumentTypeRequestValidator : AbstractValidator<DocumentTypeRequest>
{
    public DocumentTypeRequestValidator()
    {
        RuleFor(request => request.Name).NotEmpty().MaximumLength(100);
        RuleFor(request => request.ExpiryRule).IsInEnum();
    }
}

public sealed class DocumentRequirementListRequestValidator : AbstractValidator<DocumentRequirementListRequest>
{
    public DocumentRequirementListRequestValidator()
    {
        RuleFor(request => request.Items)
            .NotNull()
            .Must(items => items.Select(item => item.DocumentTypeId).Distinct().Count() == items.Count)
            .WithMessage("Each document can only appear once in a list.");
        RuleForEach(request => request.Items).ChildRules(item =>
        {
            item.RuleFor(value => value.DocumentTypeId).NotEmpty();
            item.RuleFor(value => value.Condition).IsInEnum();
        });
    }
}

public sealed class DocumentSubmitRequestValidator : AbstractValidator<DocumentSubmitRequest>
{
    public DocumentSubmitRequestValidator()
    {
        RuleFor(request => request.DocumentTypeId).NotEmpty().WithMessage("Choose the document type.");
        RuleFor(request => request.HolderName).MaximumLength(200);
        RuleFor(request => request.ReferenceNumber).MaximumLength(100);
        RuleFor(request => request.IssuingCountry)
            .Must(country => string.IsNullOrWhiteSpace(country) || DocumentCountries.Normalize(country) is not null)
            .WithMessage("Use the two-letter country code.");
        RuleFor(request => request.Stamp).MaximumLength(20);
        RuleFor(request => request.VehicleRegistration).MaximumLength(20);
        RuleFor(request => request.PenaltyPoints).InclusiveBetween(0, 100);
        RuleFor(request => request.Note).MaximumLength(500);
    }
}
