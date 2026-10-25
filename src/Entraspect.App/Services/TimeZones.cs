namespace Entraspect.App.Services;

/// <summary>
/// The time zones Settings > Preferences offers: the computer's own zone and every zone the platform knows, sorted by
/// UTC offset then name. Tests pass their own zones, so they never depend on the computer's zone.
/// </summary>
public sealed class TimeZones
{
    private readonly Lazy<IReadOnlyList<TimeZoneInfo>> _all;

    public TimeZones(TimeZoneInfo local, IEnumerable<TimeZoneInfo> zones)
        : this(local, () => zones)
    {
    }

    private TimeZones(TimeZoneInfo local, Func<IEnumerable<TimeZoneInfo>> zones)
    {
        Local = local;

        // "UTC" and "Local" have their own entries, so a platform zone with either ID isn't listed again.
        _all = new Lazy<IReadOnlyList<TimeZoneInfo>>(() => zones()
            .Where(z => !IsReserved(z.Id))
            .OrderBy(z => z.BaseUtcOffset)
            .ThenBy(z => z.DisplayName, StringComparer.CurrentCulture)
            .ToList());
    }

    /// <summary>The computer's own zones. They are only read when first needed.</summary>
    public static TimeZones FromSystem() => new(TimeZoneInfo.Local, TimeZoneInfo.GetSystemTimeZones);

    public TimeZoneInfo Local { get; }

    public IReadOnlyList<TimeZoneInfo> All => _all.Value;

    /// <summary>
    /// The zone with <paramref name="id"/>, or null if this computer doesn't know it. An IANA ID is also looked up as
    /// its Windows ID and the other way round, so settings.json moves between platforms.
    /// </summary>
    public TimeZoneInfo? Find(string id)
    {
        if (FindExact(id) is { } zone)
        {
            return zone;
        }

        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windowsId) && FindExact(windowsId) is { } fromIana)
        {
            return fromIana;
        }

        return TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var ianaId) && FindExact(ianaId) is { } fromWindows
            ? fromWindows
            : null;
    }

    private TimeZoneInfo? FindExact(string id) => All.FirstOrDefault(z => string.Equals(z.Id, id, StringComparison.Ordinal));

    private static bool IsReserved(string id) =>
        string.Equals(id, AppSettings.UtcTimeZone, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(id, AppSettings.LocalTimeZone, StringComparison.OrdinalIgnoreCase);
}
