using FluentValidation;
using Roaster_Generator.Contracts.Auth;

namespace Roaster_Generator.Validation;

public sealed class ChangeInitialPasswordRequestValidator : AbstractValidator<ChangeInitialPasswordRequest>
{
    public ChangeInitialPasswordRequestValidator()
    {
        RuleFor(request => request.NewPassword).NotEmpty().MinimumLength(5).MaximumLength(128);
        RuleFor(request => request.ConfirmPassword).NotEmpty().Equal(request => request.NewPassword)
            .WithMessage("Passwords must match.");
    }
}
