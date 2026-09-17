using FluentValidation;
using Roaster_Generator.Contracts.Roster;
using Roaster_Generator.Contracts.Schedules;
using Roaster_Generator.Services;

namespace Roaster_Generator.Validation;

public sealed class RosterSettingsRequestValidator : AbstractValidator<RosterSettingsRequest>
{
    public RosterSettingsRequestValidator()
    {
        RuleFor(request => request.CompanyCars).InclusiveBetween(0, 1000);
        RuleFor(request => request.CompanyMopeds).InclusiveBetween(0, 1000);
        RuleFor(request => request.CompanyEBikes).InclusiveBetween(0, 1000);
        RuleFor(request => request.FairHoursAlpha).InclusiveBetween(0, 1);
        RuleFor(request => request.ApproximateHoursWeight).InclusiveBetween(0, 1000);
        RuleFor(request => request.HistoryFairnessWeight).InclusiveBetween(0, 1000);
        RuleFor(request => request.HistoryShiftLengthWeight).InclusiveBetween(0, 1000);
        RuleFor(request => request.FairnessSpreadWeight).InclusiveBetween(0, 1000);
        RuleFor(request => request.LongShiftBonus).InclusiveBetween(0, 1000);
        RuleFor(request => request.ShortShiftPenalty).InclusiveBetween(0, 1000);
        RuleFor(request => request.DailyShiftCountPenalty).InclusiveBetween(0, 1000);
        RuleFor(request => request.ShortBreakPenalty).InclusiveBetween(0, 1000);
        RuleFor(request => request.MinimumRestHours).InclusiveBetween(0, 24);
        RuleFor(request => request.PreferredRestHours).InclusiveBetween(0, 48);
        RuleFor(request => request.LatestShiftStartHour).InclusiveBetween(6, 22);
        RuleFor(request => request.MaxSolveSeconds).InclusiveBetween(1, 120);

        RuleFor(request => request)
            .Custom((request, context) =>
            {
                if (request.PreferredRestHours < request.MinimumRestHours)
                {
                    context.AddFailure(
                        nameof(request.PreferredRestHours),
                        "Preferred rest must be at least the minimum rest.");
                }
            });
    }
}

public sealed class RosterPlanUpdateRequestValidator : AbstractValidator<RosterPlanUpdateRequest>
{
    public RosterPlanUpdateRequestValidator()
    {
        RuleFor(request => request.RosterKind).Must(RosterKinds.IsEnabled)
            .WithMessage(RosterKinds.InvalidMessage);
        RuleFor(request => request.WeekStart)
            .Must(date => date != DateOnly.MinValue && date.DayOfWeek == DayOfWeek.Monday)
            .WithMessage("Select the Monday of a saved roster week.");

        RuleFor(request => request.Shifts)
            .NotNull()
            .WithMessage("Roster shifts are required.");

        RuleForEach(request => request.Shifts)
            .SetValidator(new RosterShiftUpdateRequestValidator());

        RuleFor(request => request)
            .Custom((request, context) =>
            {
                if (request.WeekStart == DateOnly.MinValue ||
                    request.WeekStart.DayOfWeek != DayOfWeek.Monday ||
                    request.Shifts is null)
                {
                    return;
                }

                var weekEnd = request.WeekStart.AddDays(7);

                for (var index = 0; index < request.Shifts.Count; index++)
                {
                    var date = request.Shifts[index].Date;

                    if (date < request.WeekStart || date >= weekEnd)
                    {
                        context.AddFailure(
                            $"Shifts[{index}].Date",
                            "Roster shift dates must belong to the selected week.");
                    }
                }
            });
    }
}

public sealed class RosterShiftUpdateRequestValidator : AbstractValidator<RosterShiftUpdateRequest>
{
    public RosterShiftUpdateRequestValidator()
    {
        RuleFor(shift => shift.EmployeeId)
            .NotEqual(Guid.Empty)
            .WithMessage("Employee ID must not be empty.");

        RuleFor(shift => shift.Date)
            .NotEqual(DateOnly.MinValue)
            .WithMessage("A valid shift date is required.");

        RuleFor(shift => shift.StartHour)
            .InclusiveBetween(6, 29)
            .WithMessage("Shift start must be between 06:00 and 05:00 on the following day.");

        RuleFor(shift => shift.FinishHour)
            .InclusiveBetween(1, 48)
            .WithMessage("Shift finish must be between hour 1 and hour 48.");

        RuleFor(shift => shift)
            .Custom((shift, context) =>
            {
                if (shift.StartHour is < 6 or > 29 || shift.FinishHour is < 1 or > 48)
                {
                    return;
                }

                if (shift.FinishHour <= shift.StartHour)
                {
                    context.AddFailure(
                        nameof(shift.FinishHour),
                        "Shift finish must be after shift start; use an absolute overnight finish hour when needed.");
                    return;
                }

                var duration = shift.FinishHour - shift.StartHour;

                if (duration > 24)
                {
                    context.AddFailure(
                        nameof(shift.FinishHour),
                        "Manual shifts cannot last more than 24 hours.");
                }
            });
    }
}
