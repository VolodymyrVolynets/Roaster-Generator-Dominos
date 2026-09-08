using FluentValidation;
using Roaster_Generator.Entities;
using Roaster_Generator.Services;

namespace Roaster_Generator.Validation;

/// <summary>
/// Validates the complete input assembled for the roster solver before it builds a model.
/// Solver-specific feasibility and generated-roster checks remain in RosterSolver.
/// </summary>
public sealed class RosterSolverInputValidator : AbstractValidator<RosterSolverInput>
{
    public const int MaxEmployees = 1_000;

    public RosterSolverInputValidator()
    {
        RuleFor(input => input)
            .Custom((input, context) =>
            {
                var options = input.Options;

                if (input.WeekStart == DateOnly.MinValue || input.WeekStart > DateOnly.MaxValue.AddDays(-8))
                {
                    context.AddFailure("A valid week start date is required.");
                }

                if (options.MinimumRestHours is < 0 or > 24 ||
                    options.PreferredRestHours < options.MinimumRestHours ||
                    options.PreferredRestHours > 48)
                {
                    context.AddFailure(
                        "Minimum rest must be 0–24 hours; preferred rest must be at least the minimum and at most 48 hours.");
                }

                if (options.LatestShiftStartHour is < 6 or > 22)
                {
                    context.AddFailure("Latest shift start must be between 06:00 and 22:00.");
                }

                if (options.MaxSolveSeconds is < 1 or > 120)
                {
                    context.AddFailure("Solve time must be between 1 and 120 seconds.");
                }

                var weights = new[]
                {
                    options.TargetHoursWeight,
                    options.HistoryFairnessWeight,
                    options.HistoryShiftLengthWeight,
                    options.FairnessSpreadWeight,
                    options.LongShiftBonus,
                    options.ShortShiftPenalty,
                    options.DailyShiftCountPenalty,
                    options.ShortBreakPenalty
                };

                if (weights.Any(weight => weight is < 0 or > 10_000))
                {
                    context.AddFailure("Preference weights must be between 0 and 10000.");
                }

                if (input.Employees.GroupBy(employee => employee.Id).Any(group => group.Count() > 1))
                {
                    context.AddFailure("The employee list contains duplicate IDs.");
                }

                if (input.Employees.Any(employee => TargetHours(employee) is < 0 or > 168))
                {
                    context.AddFailure("Employee target hours must be between 0 and 168.");
                }

                if (input.Demand.Any(slot =>
                        slot.Date < input.WeekStart ||
                        slot.Date.DayNumber - input.WeekStart.DayNumber >= 7 ||
                        slot.Hour is < 0 or > 47 ||
                        slot.RequiredDrivers is < 0 or > MaxEmployees))
                {
                    context.AddFailure("Demand must contain hours 0–47 within the selected business week and 0–1000 drivers per hour.");
                }

                if (input.Demand.GroupBy(slot => AbsoluteHour(input.WeekStart, slot.Date, slot.Hour))
                    .Any(group => group.Count() > 1))
                {
                    context.AddFailure("Demand contains duplicate or overlapping real-world hours across business dates.");
                }

                if (input.Availability.GroupBy(shift => (shift.EmployeeId, shift.Date))
                    .Any(group => group.Count() > 1))
                {
                    context.AddFailure("Availability contains multiple windows for the same employee and business date. Consolidate each day into one window.");
                }

                if (input.Availability.Any(shift =>
                        shift.StartTime.Ticks % TimeSpan.TicksPerHour != 0 ||
                        shift.FinishTime.Ticks % TimeSpan.TicksPerHour != 0 ||
                        shift.StartTime == shift.FinishTime))
                {
                    context.AddFailure("Availability times must be whole hours, with different start and finish times.");
                }

                if (input.BoundaryShifts.Any(shift => shift.Finish <= shift.Start))
                {
                    context.AddFailure("A saved neighboring-week shift has an invalid start or finish.");
                }

                if ((input.History ?? []).Any(item =>
                        item.TargetHours is < 0 or > 168 || item.ScheduledHours is < 0 or > 168))
                {
                    context.AddFailure("Saved historical target and scheduled hours must be between 0 and 168.");
                }

                if ((input.History ?? []).Any(item => item.ShiftCount is < 0 ||
                    item.ShiftCount == 0 && item.ScheduledHours != 0 ||
                    item.ShiftCount > 0 && (item.ScheduledHours == 0 || item.ShiftCount > item.ScheduledHours)))
                {
                    context.AddFailure("Saved historical shift counts must match non-negative scheduled hours.");
                }

                if ((input.History ?? [])
                    .Where(item => item.WeekStart < input.WeekStart &&
                                   item.WeekStart.DayNumber >= input.WeekStart.DayNumber - 28)
                    .GroupBy(item => (item.EmployeeId, item.WeekStart))
                    .Any(group => group.Count() > 1))
                {
                    context.AddFailure("Historical employee snapshots contain duplicate employee/week records.");
                }
            });
    }

    public static IReadOnlyList<string> ValidateInput(RosterSolverInput input) =>
        new RosterSolverInputValidator()
            .Validate(input)
            .Errors
            .Select(error => error.ErrorMessage)
            .ToArray();

    private static int AbsoluteHour(DateOnly weekStart, DateOnly date, int hour) =>
        (date.DayNumber - weekStart.DayNumber) * 24 + hour;

    private static int TargetHours(Employee employee) => employee.DriverProfile?.TargetHours ?? 0;
}
