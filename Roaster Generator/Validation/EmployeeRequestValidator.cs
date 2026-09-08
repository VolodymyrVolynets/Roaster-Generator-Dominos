using FluentValidation;
using Roaster_Generator.Contracts.Employees;
using Roaster_Generator.Enums;
using Roaster_Generator.Security;

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
            .When(HasDriverRole)
            .WithMessage("Target hours must be between 3 and 168 per week.");

        RuleFor(request => request.PayrollNumber)
            .MaximumLength(64)
            .When(request => request.PayrollNumber is not null)
            .WithMessage("Payroll number cannot exceed 64 characters.");

        RuleFor(request => request.Roles)
            .NotNull()
            .Must(roles => roles is not null && roles.Count > 0)
            .WithMessage("At least one employee role is required.");

        RuleFor(request => request.Roles)
            .Must(roles => roles is not null &&
                          roles.Distinct(StringComparer.OrdinalIgnoreCase).Count() == roles.Count)
            .When(request => request.Roles is not null)
            .WithMessage("Employee roles must be unique.");

        RuleForEach(request => request.Roles)
            .Must(IsAllowedRole)
            .WithMessage("Employee roles must be Driver, InStore, Manager, or Admin.");

        RuleFor(request => request.Roles)
            .Must(roles => roles is not null && roles.Any(role =>
                string.Equals(role, RoleNames.Driver, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(role, RoleNames.InStore, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(role, RoleNames.Manager, StringComparison.OrdinalIgnoreCase)))
            .When(request => request.Roles is not null)
            .WithMessage("An employee must have a Driver, InStore, or Manager role.");

        RuleFor(request => request.DriverType)
            .IsInEnum()
            .When(HasDriverRole)
            .WithMessage("Driver type must be Car, Moped, or EBike.");
    }

    private static bool HasDriverRole(EmployeeRequest request) =>
        request.Roles?.Any(role => string.Equals(role, RoleNames.Driver, StringComparison.OrdinalIgnoreCase)) == true;

    private static bool IsAllowedRole(string? role) =>
        string.Equals(role, RoleNames.Driver, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(role, RoleNames.InStore, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(role, RoleNames.Manager, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(role, RoleNames.Admin, StringComparison.OrdinalIgnoreCase);
}
