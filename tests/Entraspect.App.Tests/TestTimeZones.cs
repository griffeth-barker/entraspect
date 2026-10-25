using Entraspect.App.Services;

namespace Entraspect.App.Tests;

/// <summary>Custom time zones, so tests never depend on the computer's own zone.</summary>
internal static class TestTimeZones
{
    public static readonly TimeZoneInfo PlusTwoHours =
        TimeZoneInfo.CreateCustomTimeZone("Test+2", TimeSpan.FromHours(2), "Test+2", "Test+2");

    /// <summary>UTC+1, or UTC+2 from the last Sunday in March to the last Sunday in October (25 October 2026).</summary>
    public static readonly TimeZoneInfo Berlin = TimeZoneInfo.CreateCustomTimeZone(
        "Europe/Berlin",
        TimeSpan.FromHours(1),
        "(UTC+01:00) Amsterdam, Berlin, Bern, Rome, Stockholm, Vienna",
        "Central European Standard Time",
        "Central European Summer Time",
        [
            TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                DateTime.MinValue.Date,
                DateTime.MaxValue.Date,
                TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 5, DayOfWeek.Sunday),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 5, DayOfWeek.Sunday)),
        ]);

    public static readonly TimeZoneInfo Tokyo = TimeZoneInfo.CreateCustomTimeZone(
        "Asia/Tokyo", TimeSpan.FromHours(9), "(UTC+09:00) Osaka, Sapporo, Tokyo", "Tokyo Standard Time");

    public static readonly TimeZoneInfo NewYork = TimeZoneInfo.CreateCustomTimeZone(
        "America/New_York", TimeSpan.FromHours(-5), "(UTC-05:00) Eastern Time (US & Canada)", "Eastern Standard Time");

    /// <summary>Local time is <see cref="PlusTwoHours"/>; the platform knows Berlin, Tokyo and New York.</summary>
    public static TimeZones Create() => new(PlusTwoHours, [Tokyo, Berlin, NewYork]);
}
