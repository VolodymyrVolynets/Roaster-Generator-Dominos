using FluentValidation;
using Roaster_Generator.Contracts.Roster;
using Roaster_Generator.Services;

namespace Roaster_Generator.Validation;

public sealed class RosterLabourRequestValidator : AbstractValidator<RosterLabourRequest>
{
    public RosterLabourRequestValidator()
    {
        RuleFor(request => request.WeekStart)
            .Must(date => date is null || date != DateOnly.MinValue && date.Value.DayOfWeek == DayOfWeek.Monday &&
                date <= DateOnly.MaxValue.AddDays(-7))
            .WithMessage("Select the Monday of a saved roster week.");
        RuleFor(request => request.WeekOffset)
            .Must(offset => offset is null or >= WeeklyScheduleService.MinWeekOffset and <= WeeklyScheduleService.MaxWeekOffset)
            .WithMessage("Select a week from next week through three weeks ahead.");
        RuleFor(request => request)
            .Must(request => !request.WeekStart.HasValue || !request.WeekOffset.HasValue)
            .WithMessage("Select either a saved week date or a future week offset, not both.");
    }
}
