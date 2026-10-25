namespace Entraspect.App.Services;

/// <summary>
/// Formats dates and times for display in UTC, the computer's time zone or a chosen zone, following
/// Settings > Preferences. Every date or time the app shows goes through here. A saved zone this computer doesn't know
/// is shown as UTC.
/// </summary>
public sealed class TimeDisplay
{
    private readonly SettingsState _settings;

    // The last zone ID looked up, so formatting a long list of dates doesn't search the zones every time.
    private string? _foundId;
    private TimeZoneInfo? _found;

    public TimeDisplay(SettingsState settings, TimeZones? zones = null)
    {
        _settings = settings;
        Zones = zones ?? TimeZones.FromSystem();
    }

    public TimeZones Zones { get; }

    /// <summary>
    /// What dates are shown in: <see cref="AppSettings.UtcTimeZone"/>, <see cref="AppSettings.LocalTimeZone"/> or the
    /// ID of a zone this computer knows. UTC when the saved zone isn't known here.
    /// </summary>
    public string EffectiveTimeZone => Zone switch
    {
        null => AppSettings.UtcTimeZone,
        _ when IsLocal => AppSettings.LocalTimeZone,
        var zone => zone.Id,
    };

    /// <summary>True when the saved time zone isn't known on this computer, so UTC is shown instead.</summary>
    public bool SavedZoneUnavailable => !IsUtc && !IsLocal && Zone is null;

    /// <summary>"UTC", "local" or the zone's ID such as "Europe/Berlin", for column headers such as "Expires (UTC)".</summary>
    public string ZoneLabel => Zone switch
    {
        null => "UTC",
        _ when IsLocal => "local",
        var zone => zone.Id,
    };

    /// <summary>
    /// For example "2026-10-24 21:13 UTC", "2026-10-24 14:13" in local time, or "2026-10-24 23:13 (UTC+02:00)" in a
    /// chosen zone, with the offset at that instant.
    /// </summary>
    public string DateTime(DateTimeOffset value) => Format(value, "yyyy-MM-dd HH:mm");

    /// <summary>The time of day, for example "21:13 UTC", "14:13" in local time, or "23:13 (UTC+02:00)".</summary>
    public string Time(DateTimeOffset value) => Format(value, "HH:mm");

    /// <summary>The date only, in the chosen zone, for example "2026-10-27".</summary>
    public string Date(DateTimeOffset value) => Convert(value).ToString("yyyy-MM-dd");

    /// <summary>For file names, in the chosen zone, for example "20261024-2113".</summary>
    public string FileStamp(DateTimeOffset value) => Convert(value).ToString("yyyyMMdd-HHmm");

    /// <summary>The computer's time zone, for example "(UTC-08:00) Pacific Time (Los Angeles)".</summary>
    public string LocalZoneName => Zones.Local.DisplayName;

    private bool IsUtc => _settings.Current.TimeZone == AppSettings.UtcTimeZone;

    private bool IsLocal => _settings.Current.TimeZone == AppSettings.LocalTimeZone;

    /// <summary>The zone dates are shown in, or null for UTC.</summary>
    private TimeZoneInfo? Zone
    {
        get
        {
            var id = _settings.Current.TimeZone;
            if (IsUtc)
            {
                return null;
            }

            if (IsLocal)
            {
                return Zones.Local;
            }

            if (id != _foundId)
            {
                _found = Zones.Find(id);
                _foundId = id;
            }

            return _found;
        }
    }

    private DateTimeOffset Convert(DateTimeOffset value) =>
        Zone is { } zone ? TimeZoneInfo.ConvertTime(value, zone) : value.ToUniversalTime();

    private string Format(DateTimeOffset value, string format)
    {
        var converted = Convert(value);
        var text = converted.ToString(format);
        if (Zone is null)
        {
            return $"{text} UTC";
        }

        return IsLocal ? text : $"{text} {OffsetText(converted.Offset)}";
    }

    /// <summary>For example "(UTC+02:00)" or "(UTC-03:30)".</summary>
    private static string OffsetText(TimeSpan offset) =>
        $"(UTC{(offset < TimeSpan.Zero ? "-" : "+")}{offset.Duration():hh\\:mm})";
}
