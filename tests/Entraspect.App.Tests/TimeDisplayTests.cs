using System.IO;
using Entraspect.App.Services;

namespace Entraspect.App.Tests;

public sealed class TimeDisplayTests : IDisposable
{
    private static readonly DateTimeOffset Value = new(2026, 10, 24, 23, 30, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo PlusTwoHours =
        TimeZoneInfo.CreateCustomTimeZone("Test+2", TimeSpan.FromHours(2), "Test+2", "Test+2");

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"entraspect-time-{Guid.NewGuid()}");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void Utc_is_the_default()
    {
        var time = new TimeDisplay(new SettingsState(new SettingsStore(_dir)), PlusTwoHours);

        Assert.Equal("2026-10-24 23:30 UTC", time.DateTime(Value));
        Assert.Equal("23:30 UTC", time.Time(Value));
        Assert.Equal("2026-10-24", time.Date(Value));
        Assert.Equal("UTC", time.ZoneLabel);
    }

    [Fact]
    public void Local_time_converts_and_can_change_the_date()
    {
        var settings = new SettingsState(new SettingsStore(_dir));
        var time = new TimeDisplay(settings, PlusTwoHours);

        settings.SetUseLocalTime(true);

        Assert.Equal("2026-10-25 01:30", time.DateTime(Value));
        Assert.Equal("01:30", time.Time(Value));
        Assert.Equal("2026-10-25", time.Date(Value));
        Assert.Equal("local", time.ZoneLabel);
        Assert.True(new SettingsStore(_dir).Load(out _).UseLocalTime);
    }
}
