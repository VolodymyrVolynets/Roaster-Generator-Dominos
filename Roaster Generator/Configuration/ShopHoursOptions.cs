namespace Roaster_Generator.Configuration;

public sealed class ShopHoursOptions
{
    public const string SectionName = "ShopHours";

    public ShopDayHoursOptions Monday { get; set; } = new();

    public ShopDayHoursOptions Tuesday { get; set; } = new();

    public ShopDayHoursOptions Wednesday { get; set; } = new();

    public ShopDayHoursOptions Thursday { get; set; } = new();

    public ShopDayHoursOptions Friday { get; set; } = new();

    public ShopDayHoursOptions Saturday { get; set; } = new();

    public ShopDayHoursOptions Sunday { get; set; } = new();

    public ShopDayHoursOptions For(DayOfWeek dayOfWeek) => dayOfWeek switch
    {
        DayOfWeek.Monday => Monday,
        DayOfWeek.Tuesday => Tuesday,
        DayOfWeek.Wednesday => Wednesday,
        DayOfWeek.Thursday => Thursday,
        DayOfWeek.Friday => Friday,
        DayOfWeek.Saturday => Saturday,
        DayOfWeek.Sunday => Sunday,
        _ => throw new ArgumentOutOfRangeException(nameof(dayOfWeek), dayOfWeek, null)
    };
}

public sealed class ShopDayHoursOptions
{
    public TimeOnly OpeningTime { get; set; } = new(12, 0);

    public TimeOnly ClosingTime { get; set; } = new(1, 0);
}
