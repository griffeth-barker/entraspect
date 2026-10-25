namespace Entraspect.App.Services;

/// <summary>
/// Formats dates and times for display in UTC or the computer's time zone, following Settings > Preferences.
/// Every date or time the app shows goes through here.
/// </summary>
public sealed class TimeDisplay
{
    private readonly SettingsState _settings;
    private readonly TimeZoneInfo _localZone;

    public TimeDisplay(SettingsState settings, TimeZoneInfo? localZone = null)
    {
        _settings = settings;
        _localZone = localZone ?? TimeZoneInfo.Local;
    }

    public bool UseLocalTime => _settings.Current.UseLocalTime;

    /// <summary>"UTC" or "local", for column headers such as "Expires (UTC)".</summary>
    public string ZoneLabel => UseLocalTime ? "local" : "UTC";

    /// <summary>For example "2026-10-24 21:13 UTC", or "2026-10-24 14:13" in local time.</summary>
    public string DateTime(DateTimeOffset value) => UseLocalTime
        ? $"{ToLocal(value):yyyy-MM-dd HH:mm}"
        : $"{value.ToUniversalTime():yyyy-MM-dd HH:mm} UTC";

    /// <summary>The time of day, for example "21:13 UTC", or "14:13" in local time.</summary>
    public string Time(DateTimeOffset value) => UseLocalTime
        ? $"{ToLocal(value):HH:mm}"
        : $"{value.ToUniversalTime():HH:mm} UTC";

    /// <summary>The date only, in the chosen zone, for example "2026-10-27".</summary>
    public string Date(DateTimeOffset value) => UseLocalTime
        ? $"{ToLocal(value):yyyy-MM-dd}"
        : $"{value.ToUniversalTime():yyyy-MM-dd}";

    /// <summary>For file names, in the chosen zone, for example "20261024-2113".</summary>
    public string FileStamp(DateTimeOffset value) => UseLocalTime
        ? $"{ToLocal(value):yyyyMMdd-HHmm}"
        : $"{value.ToUniversalTime():yyyyMMdd-HHmm}";

    /// <summary>The computer's time zone, for example "(UTC-08:00) Pacific Time (Los Angeles)".</summary>
    public string LocalZoneName => _localZone.DisplayName;

    private DateTimeOffset ToLocal(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, _localZone);
}
