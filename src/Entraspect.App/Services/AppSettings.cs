using System.Collections.Generic;
using Entraspect.Core;

namespace Entraspect.App.Services;

/// <summary>
/// Persisted application settings, saved as settings.json next to profiles.json. Never includes secrets.
/// </summary>
public sealed record AppSettings
{
    public List<string> DisabledCheckIds { get; init; } = new();

    public CheckOptions CheckOptions { get; init; } = new();

    public string? LastUsedProfileName { get; init; }

    /// <summary>Show dates and times in the computer's time zone instead of UTC (Settings > Preferences).</summary>
    public bool UseLocalTime { get; init; }
}
