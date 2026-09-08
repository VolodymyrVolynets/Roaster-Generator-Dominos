namespace Roaster_Generator.Services;

public static class DemandStaffing
{
    public const decimal DefaultDeliveriesPerDriverHour = 2.7m;
    public const decimal DefaultPizzasPerInsideHour = 20m;
    public const decimal MaximumWorkload = 1_000_000m;

    public static int? Drivers(decimal? deliveries, decimal deliveriesPerDriverHour) =>
        RequiredStaff(deliveries, deliveriesPerDriverHour);

    public static int? Inside(decimal? pizzas, decimal pizzasPerInsideHour, bool isOpen)
    {
        if (!isOpen) return null;
        var required = RequiredStaff(pizzas, pizzasPerInsideHour);
        // The manager is included in inside headcount, including quiet open hours.
        return required.HasValue ? Math.Max(1, required.Value) : null;
    }

    public static decimal AppliedHourlyRate(DayOfWeek day, decimal baseHourlyRate) =>
        day == DayOfWeek.Sunday
            ? Math.Round(baseHourlyRate * 1.25m, 2, MidpointRounding.AwayFromZero)
            : baseHourlyRate;

    public static decimal LabourCost(int hours, decimal hourlyRate) =>
        Math.Round(hours * hourlyRate, 2, MidpointRounding.AwayFromZero);

    private static int? RequiredStaff(decimal? workload, decimal productivity)
    {
        if (productivity is < 0.01m or > 1000m)
            throw new DemandValidationException("Productivity must be between 0.01 and 1000 per employee-hour.");
        if (workload is < 0m or > MaximumWorkload)
            throw new DemandValidationException("Hourly deliveries and pizzas must be between 0 and 1000000.");
        return workload.HasValue ? (int)decimal.Ceiling(workload.Value / productivity) : null;
    }
}
