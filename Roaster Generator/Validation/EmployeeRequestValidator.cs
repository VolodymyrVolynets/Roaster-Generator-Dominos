using FluentValidation;
using Roaster_Generator.Contracts.Employees;

namespace Roaster_Generator.Validation;

public sealed class EmployeeRequestValidator : AbstractValidator<EmployeeRequest>
{
    public EmployeeRequestValidator()
    {
        RuleFor(request => request.EmployeeNumber)
            .Must(value => !string.IsNullOrWhiteSpace(value))
            .WithMessage("Employee number is required.");

        RuleFor(request => request.FirstName)
            .Must(value => !string.IsNullOrWhiteSpace(value))
            .WithMessage("First name is required.");

        RuleFor(request => request.LastName)
            .Must(value => !string.IsNullOrWhiteSpace(value))
            .WithMessage("Last name is required.");

        RuleFor(request => request.TargetHours)
            .InclusiveBetween(3, 168)
            .WithMessage("Target hours must be between 3 and 168 per week.");
    }
}
