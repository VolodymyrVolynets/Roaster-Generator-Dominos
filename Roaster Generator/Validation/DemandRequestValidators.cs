using FluentValidation;
using Roaster_Generator.Contracts.Demand;
using Roaster_Generator.Services;

namespace Roaster_Generator.Validation;

public sealed class DemandImportRequestValidator : AbstractValidator<DemandImportRequest>
{
    public DemandImportRequestValidator()
    {
        RuleFor(request => request.Name)
            .Must(value => !string.IsNullOrWhiteSpace(value))
            .WithMessage("A demand plan name is required.");

        RuleFor(request => request.WeekStart)
            .NotEqual(DateOnly.MinValue)
            .WithMessage("A week start date is required.");

        RuleFor(request => request.WeekStart)
            .Must(date => date.DayOfWeek == DayOfWeek.Monday)
            .When(request => request.WeekStart != DateOnly.MinValue)
            .WithMessage("The week start date must be a Monday.");

        RuleFor(request => request.Content)
            .Must(value => !string.IsNullOrWhiteSpace(value))
            .WithMessage("Paste the demand table before importing it.");
    }
}

public sealed class DemandPlanUpdateRequestValidator : AbstractValidator<DemandPlanUpdateRequest>
{
    public DemandPlanUpdateRequestValidator()
    {
        RuleFor(request => request.Name)
            .Must(value => !string.IsNullOrWhiteSpace(value))
            .WithMessage("A demand plan name is required.");

        RuleFor(request => request.WeekStart)
            .NotEqual(DateOnly.MinValue)
            .WithMessage("A week start date is required.");

        RuleFor(request => request.WeekStart)
            .Must(date => date.DayOfWeek == DayOfWeek.Monday)
            .When(request => request.WeekStart != DateOnly.MinValue)
            .WithMessage("The week start date must be a Monday.");

        RuleFor(request => request.DeliveriesPerDriverHour)
            .InclusiveBetween(0.01m, 1000m)
            .WithMessage("Deliveries per driver-hour must be between 0.01 and 1000.");

        RuleFor(request => request.Columns)
            .NotNull()
            .WithMessage("Demand columns are required.")
            .Must(columns => columns is not null && columns.Count == 7)
            .WithMessage("The demand plan must contain exactly seven day columns.");

        RuleFor(request => request.Columns)
            .Must(columns => columns is not null &&
                             columns.Select(column => column.Position).Distinct().Count() == columns.Count)
            .When(request => request.Columns is not null)
            .WithMessage("Demand column positions must be unique.");

        RuleForEach(request => request.Columns)
            .SetValidator(new DemandColumnRequestValidator());

        RuleFor(request => request.Rows)
            .NotNull()
            .WithMessage("Demand rows are required.");

        RuleFor(request => request.Rows)
            .Must(rows => rows is not null &&
                          rows.Select(row => row.Hour).Distinct().Count() == rows.Count)
            .When(request => request.Rows is not null)
            .WithMessage("Demand hours must be unique.");

        RuleForEach(request => request.Rows)
            .SetValidator(new DemandRowRequestValidator());

        RuleFor(request => request)
            .Custom((request, context) =>
            {
                if (request.Columns is null || request.Rows is null)
                {
                    return;
                }

                var positions = request.Columns.Select(column => column.Position).ToHashSet();

                for (var index = 0; index < request.Rows.Count; index++)
                {
                    var values = request.Rows[index].Values ?? [];

                    if (!positions.SetEquals(values.Select(value => value.Position)))
                    {
                        context.AddFailure(
                            $"Rows[{index}].Values",
                            "Every demand row must contain each imported day exactly once.");
                    }
                }
            });
    }
}

public sealed class DemandColumnRequestValidator : AbstractValidator<DemandColumnRequest>
{
    public DemandColumnRequestValidator()
    {
        RuleFor(column => column.Position)
            .InclusiveBetween(0, 6)
            .WithMessage("Demand column positions must be between 0 and 6.");

        RuleFor(column => column.TargetSales)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Target sales cannot be negative.");
    }
}

public sealed class DemandRowRequestValidator : AbstractValidator<DemandRowRequest>
{
    public DemandRowRequestValidator()
    {
        RuleFor(row => row.Hour)
            .InclusiveBetween(0, 23)
            .WithMessage("Demand hours must be between 00 and 23.");

        RuleFor(row => row.Values)
            .NotNull()
            .WithMessage("Demand values are required.");

        RuleFor(row => row.Values)
            .Must(values => values is not null &&
                            values.Select(value => value.Position).Distinct().Count() == values.Count)
            .When(row => row.Values is not null)
            .WithMessage("Demand value positions must be unique.");

        RuleForEach(row => row.Values)
            .SetValidator(new DemandValueRequestValidator());
    }
}

public sealed class DemandValueRequestValidator : AbstractValidator<DemandValueRequest>
{
    public DemandValueRequestValidator()
    {
        RuleFor(value => value.Position)
            .InclusiveBetween(0, 6)
            .WithMessage("Demand value positions must be between 0 and 6.");

        RuleFor(value => value.Demand)
            .GreaterThanOrEqualTo(0)
            .When(value => value.Demand.HasValue)
            .WithMessage("Outside demand cannot be negative.");

        RuleFor(value => value.InsideDemand)
            .GreaterThanOrEqualTo(0)
            .When(value => value.InsideDemand.HasValue)
            .WithMessage("Inside demand cannot be negative.");

        RuleFor(value => value.Deliveries)
            .InclusiveBetween(0m, DemandStaffing.MaximumWorkload)
            .When(value => value.Deliveries.HasValue)
            .WithMessage("Hourly deliveries must be between 0 and 1000000.");

    }
}
