using FluentValidation;
using Microsoft.Extensions.Options;
using Roaster_Generator.Configuration;
using Roaster_Generator.Contracts.Schedules;
using Roaster_Generator.Services;
using System.Globalization;

namespace Roaster_Generator.Validation;

public sealed class WeeklyScheduleRequestValidator : AbstractValidator<WeeklyScheduleRequest>
{
    public const string DriverLimitsRuleSet = "DriverAvailabilityLimits";
    public const int MaximumDriverHoursPerDay = 10;
    public const int MaximumDriverDaysPerWeek = 6;
    private readonly ShopHoursOptions shopHours;

    public WeeklyScheduleRequestValidator(IOptions<ShopHoursOptions> shopHoursOptions)
    {
        shopHours = shopHoursOptions.Value;

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

                if (!WeeklyScheduleService.IsValidWeekOffset(request.WeekOffset))
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

        // The controller selects this rule set from the authenticated user's roles.
        // Admins still receive all ordinary date, time and shop-hours validation.
        RuleSet(DriverLimitsRuleSet, () =>
            RuleFor(request => request).Custom(ValidateDriverLimits));
    }

    private static void ValidateDriverLimits(WeeklyScheduleRequest request,
        ValidationContext<WeeklyScheduleRequest> context)
    {
        if (request.Days is null) return;
        if (request.Days.Count(day => day.StartTime is not null || day.FinishTime is not null) > MaximumDriverDaysPerWeek)
            context.AddFailure(nameof(request.Days),
                $"Drivers can enter availability for at most {MaximumDriverDaysPerWeek} days per week. An administrator can make exceptions.");

        for (var index = 0; index < request.Days.Count; index++)
        {
            var day = request.Days[index];
            if (day.StartTime is not { } start || day.FinishTime is not { } finish || start == finish) continue;
            var duration = finish.ToTimeSpan() - start.ToTimeSpan();
            if (duration < TimeSpan.Zero) duration += TimeSpan.FromDays(1);
            if (duration > TimeSpan.FromHours(MaximumDriverHoursPerDay))
                context.AddFailure($"Days[{index}].FinishTime",
                    $"Driver availability cannot exceed {MaximumDriverHoursPerDay} hours per day, including overnight hours. An administrator can make exceptions.");
        }
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

public sealed class AdminWeekSelectionRequestValidator : AbstractValidator<WeekSelectionRequest>
{
    public AdminWeekSelectionRequestValidator()
    {
        RuleFor(request => request.WeekOffset)
            .Must(WeeklyScheduleService.IsValidWeekOffset)
            .WithMessage("Select a valid availability week.");
    }
}

public sealed class AdminEditableWeekSelectionRequestValidator : AbstractValidator<WeekSelectionRequest>
{
    public AdminEditableWeekSelectionRequestValidator()
    {
        RuleFor(request => request.WeekOffset)
            .Must(offset => offset <= WeeklyScheduleService.MaxWeekOffset &&
                            WeeklyScheduleService.IsValidWeekOffset(offset))
            .WithMessage("Administrators can edit past and current weeks through three weeks ahead.");
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
