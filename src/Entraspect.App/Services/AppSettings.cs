using System.Collections.Generic;
using Entraspect.Core;

namespace Entraspect.App.Services;

/// <summary>
/// Persisted application settings, saved as settings.json next to profiles.json. Never includes secrets.
/// </summary>
public sealed record AppSettings
{
    /// <summary><see cref="TimeZone"/> value for UTC, the default.</summary>
    public const string UtcTimeZone = "UTC";

    /// <summary><see cref="TimeZone"/> value for the computer's own time zone.</summary>
    public const string LocalTimeZone = "Local";

    public List<string> DisabledCheckIds { get; init; } = new();

    public CheckOptions CheckOptions { get; init; } = new();

    public string? LastUsedProfileName { get; init; }

    /// <summary>
    /// Where dates and times are shown (Settings > Preferences): <see cref="UtcTimeZone"/>, <see cref="LocalTimeZone"/>,
    /// or a time zone ID (IANA where the platform has one, otherwise Windows).
    /// </summary>
    public string TimeZone { get; init; } = UtcTimeZone;
}
