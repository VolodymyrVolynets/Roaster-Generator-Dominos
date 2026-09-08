using FluentValidation;
using Roaster_Generator.Contracts.Holidays;

namespace Roaster_Generator.Validation;

public sealed class HolidayRequestValidator : AbstractValidator<HolidayHoursRequest>
{
    public const int MaxHolidayHours = 80;

    public HolidayRequestValidator()
    {
        RuleFor(request => request.Hours)
            .InclusiveBetween(1, MaxHolidayHours)
            .WithMessage($"Holiday hours must be between 1 and {MaxHolidayHours}.");
    }
}
