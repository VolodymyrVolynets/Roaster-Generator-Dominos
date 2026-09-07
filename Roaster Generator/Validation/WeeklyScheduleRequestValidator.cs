using FluentValidation;
using Roaster_Generator.Contracts.Schedules;
using Roaster_Generator.Services;

namespace Roaster_Generator.Validation;

public sealed class WeeklyScheduleRequestValidator : AbstractValidator<WeeklyScheduleRequest>
{
    public WeeklyScheduleRequestValidator()
    {
        RuleFor(request => request.Days)
            .NotNull()
            .WithMessage("Seven schedule days are required.")
            .Must(days => days is not null && days.Count == 7)
            .WithMessage("Exactly seven schedule days are required.");

        RuleForEach(request => request.Days)
            .SetValidator(new ScheduleDayRequestValidator());

        RuleFor(request => request.Days)
            .Custom((days, context) =>
            {
                if (days is null || days.Count != 7)
                {
                    return;
                }

                var weekStart = WeeklyScheduleService.GetNextWeekMonday();

                for (var index = 0; index < days.Count; index++)
                {
                    var expectedDate = weekStart.AddDays(index);

                    if (days[index].Date != expectedDate)
                    {
                        context.AddFailure(
                            $"Days[{index}].Date",
                            $"Date must be {expectedDate:yyyy-MM-dd} for the next-week schedule.");
                    }
                }
            });
    }
}

public sealed class ScheduleDayRequestValidator : AbstractValidator<ScheduleDayRequest>
{
    public ScheduleDayRequestValidator()
    {
        RuleFor(day => day.Date)
            .NotEqual(DateOnly.MinValue)
            .WithMessage("A valid date is required.");

        RuleFor(day => day)
            .Custom((day, context) =>
            {
                if (day.StartTime is null && day.FinishTime is null)
                {
                    return;
                }

                if (day.StartTime is null || day.FinishTime is null)
                {
                    context.AddFailure(
                        "StartTime",
                        "Start and finish times must be supplied together.");
                    return;
                }

                if (day.StartTime.Value.Minute != 0 || day.FinishTime.Value.Minute != 0)
                {
                    context.AddFailure(
                        "StartTime",
                        "Shift times must use whole hours, for example 09:00.");
                }

                if (day.FinishTime <= day.StartTime)
                {
                    context.AddFailure(
                        "FinishTime",
                        "Finish time must be after start time.");
                }
            });
    }
}
