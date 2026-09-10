namespace Roaster_Generator.Services;

public sealed class AvailabilityEditPolicy(TimeProvider timeProvider)
{
    public const string WeekendLockMessage =
        "Availability for next week is locked from Saturday. Select the week after next or three weeks ahead.";

    private static readonly TimeZoneInfo ShopTimeZone = ResolveShopTimeZone();

    public int GetMinimumEditableWeekOffset(bool isAdmin)
    {
        if (isAdmin) return WeeklyScheduleService.MinWeekOffset;

        var shopNow = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), ShopTimeZone);
        return shopNow.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
            ? WeeklyScheduleService.MinWeekOffset + 1
            : WeeklyScheduleService.MinWeekOffset;
    }

    private static TimeZoneInfo ResolveShopTimeZone()
    {
        foreach (var id in new[] { "Europe/Dublin", "GMT Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                // Try the platform-specific identifier below.
            }
            catch (InvalidTimeZoneException)
            {
                // Try the platform-specific identifier below.
            }
        }

        return TimeZoneInfo.Utc;
    }
}
