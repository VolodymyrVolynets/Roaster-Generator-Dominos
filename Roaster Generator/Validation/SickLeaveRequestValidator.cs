using FluentValidation;
using Roaster_Generator.Contracts.SickLeave;
using Roaster_Generator.Services;

namespace Roaster_Generator.Validation;

public sealed class SickLeaveRequestValidator : AbstractValidator<SickLeaveCreateRequest>
{
    public SickLeaveRequestValidator()
    {
        RuleFor(request => request.StartDate).NotEmpty();
        RuleFor(request => request.FinishDate)
            .NotEmpty()
            .GreaterThanOrEqualTo(request => request.StartDate)
            .WithMessage("Finish date must be on or after the start date.");
        RuleFor(request => request)
            .Must(request => request.StartDate == default || request.FinishDate == default ||
                             request.FinishDate.DayNumber - request.StartDate.DayNumber <= 365)
            .WithName(nameof(SickLeaveCreateRequest.FinishDate))
            .WithMessage("A sick leave request cannot be longer than 366 days.");
        RuleFor(request => request.File)
            .NotNull().WithMessage("A sick note is required.")
            .Must(file => file is null || file.Length > 0).WithMessage("The sick note is empty.")
            .Must(file => file is null || file.Length <= SickNoteFileValidator.MaxFileSize)
            .WithMessage("The sick note must be 10 MB or smaller.");
    }
}
