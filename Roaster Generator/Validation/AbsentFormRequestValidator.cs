using System.Globalization;
using FluentValidation;
using Roaster_Generator.Contracts.Absent;

namespace Roaster_Generator.Validation;

public sealed class AbsentFormRequestValidator : AbstractValidator<AbsentFormCreateRequest>
{
    public AbsentFormRequestValidator()
    {
        RuleFor(request => request).Custom((request, context) =>
            ValidateShift(request.SavedRosterShiftId, request.ShiftDate, request.ShiftStartTime,
                request.ShiftFinishTime, false, context));
        RuleFor(request => request.NotificationDate).NotEmpty();
        RuleFor(request => request.NotificationTime)
            .Must(IsWholeHour)
            .WithMessage("Select a notification hour in 24-hour format.");
        RuleFor(request => request.NotificationMethod).NotEmpty().MaximumLength(100);
        RuleFor(request => request.CancellationReason).NotEmpty().MaximumLength(2000);
    }

    internal static bool IsWholeHour(string? value) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var time) && time.Minute == 0;

    internal static void ValidateShift<T>(Guid? savedShiftId, DateOnly? shiftDate,
        string? shiftStartTime, string? shiftFinishTime, bool allowPreserve,
        ValidationContext<T> context)
    {
        var hasSavedShift = savedShiftId is Guid id && id != Guid.Empty;
        var hasAnyManualValue = shiftDate is not null || !string.IsNullOrWhiteSpace(shiftStartTime) ||
                                !string.IsNullOrWhiteSpace(shiftFinishTime);
        var hasCompleteManualShift = shiftDate is not null &&
                                     !string.IsNullOrWhiteSpace(shiftStartTime) &&
                                     !string.IsNullOrWhiteSpace(shiftFinishTime);

        if (savedShiftId == Guid.Empty)
            context.AddFailure(nameof(AbsentFormCreateRequest.SavedRosterShiftId), "Select a valid saved roster shift.");
        if (hasSavedShift && hasAnyManualValue)
        {
            context.AddFailure(nameof(AbsentFormCreateRequest.SavedRosterShiftId),
                "Choose a saved roster shift or enter a shift manually, not both.");
            return;
        }
        if (!hasSavedShift && !hasAnyManualValue && !allowPreserve)
        {
            context.AddFailure(nameof(AbsentFormCreateRequest.SavedRosterShiftId),
                "Choose a saved roster shift or enter the shift manually.");
            return;
        }
        if (!hasSavedShift && hasAnyManualValue && !hasCompleteManualShift)
        {
            context.AddFailure(nameof(AbsentFormCreateRequest.ShiftDate),
                "Shift date, start hour and finish hour are all required for a manually entered shift.");
            return;
        }
        if (!hasCompleteManualShift) return;

        if (shiftDate == DateOnly.MinValue)
            context.AddFailure(nameof(AbsentFormCreateRequest.ShiftDate), "Select a valid shift date.");
        if (!IsWholeHour(shiftStartTime))
            context.AddFailure(nameof(AbsentFormCreateRequest.ShiftStartTime), "Select a start hour in 24-hour format.");
        if (!IsWholeHour(shiftFinishTime))
            context.AddFailure(nameof(AbsentFormCreateRequest.ShiftFinishTime), "Select a finish hour in 24-hour format.");
        if (IsWholeHour(shiftStartTime) && IsWholeHour(shiftFinishTime) && shiftStartTime == shiftFinishTime)
            context.AddFailure(nameof(AbsentFormCreateRequest.ShiftFinishTime),
                "Start and finish hours must be different.");
    }
}

public sealed class AbsentFormUpdateRequestValidator : AbstractValidator<AbsentFormUpdateRequest>
{
    public AbsentFormUpdateRequestValidator()
    {
        RuleFor(request => request).Custom((request, context) =>
            AbsentFormRequestValidator.ValidateShift(request.SavedRosterShiftId, request.ShiftDate,
                request.ShiftStartTime, request.ShiftFinishTime, true, context));
        RuleFor(request => request.NotificationDate).NotEmpty();
        RuleFor(request => request.NotificationTime)
            .Must(AbsentFormRequestValidator.IsWholeHour)
            .WithMessage("Select a notification hour in 24-hour format.");
        RuleFor(request => request.NotificationMethod).NotEmpty().MaximumLength(100);
        RuleFor(request => request.CancellationReason).NotEmpty().MaximumLength(2000);
    }
}
