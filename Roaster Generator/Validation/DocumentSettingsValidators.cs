using FluentValidation;
using Roaster_Generator.Contracts.Documents;

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
