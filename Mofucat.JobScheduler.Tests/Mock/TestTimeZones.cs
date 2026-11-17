namespace Mofucat.JobScheduler.Tests.Mock;

public static class TestTimeZones
{
    public static TimeZoneInfo Tokyo { get; } = TimeZoneInfo.CreateCustomTimeZone("Test/Tokyo", TimeSpan.FromHours(9), "Test/Tokyo", "Test/Tokyo");

    public static TimeZoneInfo Eastern { get; } = CreateEastern();

    private static TimeZoneInfo CreateEastern()
    {
        var start = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0, DateTimeKind.Unspecified), 3, 2, DayOfWeek.Sunday);
        var end = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0, DateTimeKind.Unspecified), 11, 1, DayOfWeek.Sunday);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(DateTime.MinValue.Date, DateTime.MaxValue.Date, TimeSpan.FromHours(1), start, end);
        return TimeZoneInfo.CreateCustomTimeZone("Test/Eastern", TimeSpan.FromHours(-5), "Test/Eastern", "Test/Eastern Standard", "Test/Eastern Daylight", [rule]);
    }
}
