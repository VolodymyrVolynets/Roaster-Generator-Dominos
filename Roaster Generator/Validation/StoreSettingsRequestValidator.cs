using FluentValidation;
using Roaster_Generator.Contracts.Settings;

namespace Roaster_Generator.Validation;

public sealed class StoreSettingsRequestValidator : AbstractValidator<StoreSettingsRequest>
{
    public StoreSettingsRequestValidator()
    {
        RuleFor(request => request.StoreId).NotEmpty().MaximumLength(100);
        RuleFor(request => request.StoreName).NotEmpty().MaximumLength(200);
    }
}
