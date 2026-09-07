using FluentValidation;
using Microsoft.Extensions.Options;
using Roaster_Generator.Configuration;
using Roaster_Generator.Contracts.Schedules;
using Roaster_Generator.Services;
using System.Globalization;

namespace Roaster_Generator.Validation;

public sealed class WeeklyScheduleRequestValidator : AbstractValidator<WeeklyScheduleRequest>
{
    private readonly ShopHoursOptions shopHours;

    public WeeklyScheduleRequestValidator(IOptions<ShopHoursOptions> shopHoursOptions)
    {
        shopHours = shopHoursOptions.Value;

        RuleFor(request => request.WeekOffset)
            .InclusiveBetween(
                WeeklyScheduleService.MinWeekOffset,
                WeeklyScheduleService.MaxWeekOffset)
            .WithMessage("Only weeks from next week through three weeks ahead can be edited.");

        RuleFor(request => request.Days)
            .NotNull()
            .WithMessage("Seven schedule days are required.")
            .Must(days => days is not null && days.Count == 7)
            .WithMessage("Exactly seven schedule days are required.");

        RuleForEach(request => request.Days)
            .SetValidator(new ScheduleDayRequestValidator());

        RuleFor(request => request)
            .Custom((request, context) =>
            {
                var days = request.Days;

                if (days is null || days.Count != 7)
                {
                    return;
                }

                if (request.WeekOffset is < WeeklyScheduleService.MinWeekOffset
            or > WeeklyScheduleService.MaxWeekOffset)
                {
                    return;
                }

                var weekStart = WeeklyScheduleService.GetWeekMonday(request.WeekOffset);

                for (var index = 0; index < days.Count; index++)
                {
                    var expectedDate = weekStart.AddDays(index);

                    if (days[index].Date != expectedDate)
                    {
                        context.AddFailure(
                            $"Days[{index}].Date",
                            $"Date must be {expectedDate:yyyy-MM-dd} for the selected week.");
                    }

                    ValidateShopHours(days[index], index, context);
                }
            });
    }

    
    private void ValidateShopHours(
        ScheduleDayRequest day,
        int dayIndex,
        ValidationContext<WeeklyScheduleRequest> context)
    {
        if (day.StartTime is null || day.FinishTime is null ||
            day.StartTime.Value.Minute != 0 || day.FinishTime.Value.Minute != 0)
        {
            return;
        }

        if (day.StartTime == day.FinishTime)
        {
            return;
        }

        var hours = shopHours.For(day.Date.DayOfWeek);
        var startMinutes = ToMinutes(day.StartTime.Value);
        var finishMinutes = ToMinutes(day.FinishTime.Value);
        var openingMinutes = ToMinutes(hours.OpeningTime);
        var closingMinutes = ToMinutes(hours.ClosingTime);

        if (startMinutes < openingMinutes)
        {
            context.AddFailure(
                $"Days[{dayIndex}].StartTime",
                $"Start time cannot be earlier than the shop opening time ({FormatTime(hours.OpeningTime)}).");
        }

        var finishOnTimeline = finishMinutes <= startMinutes
            ? finishMinutes + 24 * 60
            : finishMinutes;
        var closingOnTimeline = closingMinutes <= openingMinutes
            ? closingMinutes + 24 * 60
            : closingMinutes;

        if (finishOnTimeline > closingOnTimeline)
        {
            context.AddFailure(
                $"Days[{dayIndex}].FinishTime",
                $"Finish time cannot be after the shop closing time ({FormatTime(hours.ClosingTime)}).");
        }
    }

    private static int ToMinutes(TimeOnly time) => time.Hour * 60 + time.Minute;

    private static string FormatTime(TimeOnly time) =>
        time.ToString("HH:mm", CultureInfo.InvariantCulture);
}

public sealed class WeekSelectionRequestValidator : AbstractValidator<WeekSelectionRequest>
{
    public WeekSelectionRequestValidator()
    {
        RuleFor(request => request.WeekOffset)
            .InclusiveBetween(
                WeeklyScheduleService.MinWeekOffset,
                WeeklyScheduleService.MaxWeekOffset)
            .WithMessage("Only weeks from next week through three weeks ahead can be viewed.");
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

                if (day.FinishTime == day.StartTime)
                {
                    context.AddFailure(
                        "FinishTime",
                        "Start and finish times must be different. For an overnight shift, use a finish time earlier than the start time.");
                }
            });
    }
}
