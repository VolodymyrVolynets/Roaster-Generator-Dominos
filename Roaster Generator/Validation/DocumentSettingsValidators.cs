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
        RuleFor(request => request.Kind).IsInEnum();
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

public sealed class DocumentRedoRequestValidator : AbstractValidator<DocumentRedoRequest>
{
    public DocumentRedoRequestValidator() => RuleFor(request => request.Reason).MaximumLength(500);
}

public sealed class DocumentAdminUpdateRequestValidator : AbstractValidator<DocumentAdminUpdateRequest>
{
    public DocumentAdminUpdateRequestValidator() => Include(new DocumentFieldsValidator());
}

public sealed class VehicleChangeRequestValidator : AbstractValidator<VehicleChangeRequest>
{
    public VehicleChangeRequestValidator()
    {
        RuleFor(request => request.Registration)
            .NotEmpty().WithMessage("Enter the new registration.")
            .MaximumLength(20)
            .Must(registration => DocumentChecks.NormalizeRegistration(registration ?? string.Empty).Length >= 2)
            .WithMessage("Enter a valid registration.");
    }
}

public sealed class DocumentTemplateFieldsRequestValidator : AbstractValidator<DocumentTemplateFieldsRequest>
{
    public DocumentTemplateFieldsRequestValidator()
    {
        RuleForEach(request => request.Fields).ChildRules(field =>
        {
            field.RuleFor(value => value.Name).NotEmpty();
            field.RuleFor(value => value.Label).NotEmpty().MaximumLength(100);
            field.RuleFor(value => value.Source).IsInEnum();
        });
    }
}
