namespace Roaster_Generator.Services;

public static class DemandStaffing
{
    public const decimal DefaultDeliveriesPerDriverHour = 2.7m;
    public const decimal MaximumWorkload = 1_000_000m;

    public static int? Drivers(decimal? deliveries, decimal deliveriesPerDriverHour) =>
        RequiredStaff(deliveries, deliveriesPerDriverHour);

    private static int? RequiredStaff(decimal? workload, decimal productivity)
    {
        if (productivity is < 0.01m or > 1000m)
            throw new DemandValidationException("Productivity must be between 0.01 and 1000 per employee-hour.");
        if (workload is < 0m or > MaximumWorkload)
            throw new DemandValidationException("Hourly deliveries must be between 0 and 1000000.");
        return workload.HasValue ? (int)decimal.Ceiling(workload.Value / productivity) : null;
    }
}
