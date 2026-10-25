using System.IO;
using Entraspect.App.Services;

namespace Entraspect.App.Tests;

public sealed class TimeDisplayTests : IDisposable
{
    private static readonly DateTimeOffset Value = new(2026, 10, 24, 23, 30, 0, TimeSpan.Zero);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"entraspect-time-{Guid.NewGuid()}");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void Utc_is_the_default()
    {
        var time = new TimeDisplay(new SettingsState(new SettingsStore(_dir)), TestTimeZones.Create());

        Assert.Equal("2026-10-24 23:30 UTC", time.DateTime(Value));
        Assert.Equal("23:30 UTC", time.Time(Value));
        Assert.Equal("2026-10-24", time.Date(Value));
        Assert.Equal("20261024-2330", time.FileStamp(Value));
        Assert.Equal("UTC", time.ZoneLabel);
        Assert.Equal(AppSettings.UtcTimeZone, time.EffectiveTimeZone);
    }

    [Fact]
    public void Local_time_converts_and_can_change_the_date()
    {
        var settings = new SettingsState(new SettingsStore(_dir));
        var time = new TimeDisplay(settings, TestTimeZones.Create());

        settings.SetTimeZone(AppSettings.LocalTimeZone);

        Assert.Equal("2026-10-25 01:30", time.DateTime(Value));
        Assert.Equal("01:30", time.Time(Value));
        Assert.Equal("2026-10-25", time.Date(Value));
        Assert.Equal("20261025-0130", time.FileStamp(Value));
        Assert.Equal("local", time.ZoneLabel);
        Assert.Equal(AppSettings.LocalTimeZone, new SettingsStore(_dir).Load(out _).TimeZone);
    }

    [Fact]
    public void A_chosen_zone_shows_its_offset_on_each_side_of_daylight_saving()
    {
        var settings = new SettingsState(new SettingsStore(_dir));
        var time = new TimeDisplay(settings, TestTimeZones.Create());

        settings.SetTimeZone("Europe/Berlin");

        // Summer time until 01:00 UTC on 25 October 2026, standard time after.
        var summer = new DateTimeOffset(2026, 10, 24, 12, 13, 0, TimeSpan.Zero);
        var winter = new DateTimeOffset(2026, 10, 25, 12, 13, 0, TimeSpan.Zero);
        Assert.Equal("2026-10-24 14:13 (UTC+02:00)", time.DateTime(summer));
        Assert.Equal("2026-10-25 13:13 (UTC+01:00)", time.DateTime(winter));
        Assert.Equal("14:13 (UTC+02:00)", time.Time(summer));
        Assert.Equal("13:13 (UTC+01:00)", time.Time(winter));
        Assert.Equal("2026-10-25", time.Date(Value));
        Assert.Equal("20261025-0130", time.FileStamp(Value));
        Assert.Equal("Europe/Berlin", time.ZoneLabel);
        Assert.Equal("Europe/Berlin", time.EffectiveTimeZone);
        Assert.False(time.SavedZoneUnavailable);
    }

    [Fact]
    public void A_negative_offset_is_shown_with_a_minus_sign()
    {
        var settings = new SettingsState(new SettingsStore(_dir));
        var time = new TimeDisplay(settings, TestTimeZones.Create());

        settings.SetTimeZone("America/New_York");

        Assert.Equal("2026-10-24 18:30 (UTC-05:00)", time.DateTime(Value));
    }

    [Fact]
    public void A_windows_id_finds_the_iana_zone()
    {
        var settings = new SettingsState(new SettingsStore(_dir));
        var time = new TimeDisplay(settings, TestTimeZones.Create());

        settings.SetTimeZone("W. Europe Standard Time");

        Assert.Equal("Europe/Berlin", time.EffectiveTimeZone);
        Assert.Equal("2026-10-25 01:30 (UTC+02:00)", time.DateTime(Value));
    }

    [Fact]
    public void An_iana_id_finds_the_windows_zone()
    {
        var tokyo = TimeZoneInfo.CreateCustomTimeZone(
            "Tokyo Standard Time", TimeSpan.FromHours(9), "(UTC+09:00) Osaka, Sapporo, Tokyo", "Tokyo Standard Time");
        var zones = new TimeZones(TestTimeZones.PlusTwoHours, [tokyo]);

        Assert.Same(tokyo, zones.Find("Asia/Tokyo"));
    }

    [Fact]
    public void An_unknown_zone_shows_utc()
    {
        var settings = new SettingsState(new SettingsStore(_dir));
        var time = new TimeDisplay(settings, TestTimeZones.Create());

        settings.SetTimeZone("Mars/Olympus_Mons");

        Assert.True(time.SavedZoneUnavailable);
        Assert.Equal(AppSettings.UtcTimeZone, time.EffectiveTimeZone);
        Assert.Equal("2026-10-24 23:30 UTC", time.DateTime(Value));
        Assert.Equal("UTC", time.ZoneLabel);
    }

    [Fact]
    public void Zones_are_sorted_by_offset_then_name()
    {
        var zones = TestTimeZones.Create();

        Assert.Equal(["America/New_York", "Europe/Berlin", "Asia/Tokyo"], zones.All.Select(z => z.Id));
    }

    [Fact]
    public void Platform_zones_named_utc_or_local_are_not_listed_twice()
    {
        var utc = TimeZoneInfo.CreateCustomTimeZone("UTC", TimeSpan.Zero, "(UTC) Coordinated Universal Time", "UTC");
        var zones = new TimeZones(TestTimeZones.PlusTwoHours, [utc, TestTimeZones.Tokyo]);

        Assert.Equal(["Asia/Tokyo"], zones.All.Select(z => z.Id));
    }
}
