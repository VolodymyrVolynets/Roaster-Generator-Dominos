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

public sealed class DocumentFieldsValidator : AbstractValidator<DocumentFields>
{
    public DocumentFieldsValidator()
    {
        RuleFor(fields => fields.HolderName).MaximumLength(200);
        RuleFor(fields => fields.ReferenceNumber).MaximumLength(100);
        RuleFor(fields => fields.IssuingCountry)
            .Must(country => string.IsNullOrWhiteSpace(country) || DocumentCountries.Normalize(country) is not null)
            .WithMessage("Use the two-letter country code.");
        RuleFor(fields => fields.Stamp).MaximumLength(20);
        RuleFor(fields => fields.VehicleRegistration).MaximumLength(20);
        RuleFor(fields => fields.PenaltyPoints).InclusiveBetween(0, 100);
    }
}

public sealed class DocumentSubmitRequestValidator : AbstractValidator<DocumentSubmitRequest>
{
    public DocumentSubmitRequestValidator()
    {
        Include(new DocumentFieldsValidator());
        RuleFor(request => request.DocumentTypeId).NotEmpty().WithMessage("Choose the document type.");
        RuleFor(request => request.Note).MaximumLength(500);
    }
}

public sealed class DocumentAdminUpdateRequestValidator : AbstractValidator<DocumentAdminUpdateRequest>
{
    public DocumentAdminUpdateRequestValidator() => Include(new DocumentFieldsValidator());
}
