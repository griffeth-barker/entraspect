using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Entraspect.App.Services;

namespace Entraspect.App.ViewModels;

/// <summary>An entry in the "Show dates and times in" drop-down.</summary>
/// <param name="Id"><see cref="AppSettings.UtcTimeZone"/>, <see cref="AppSettings.LocalTimeZone"/> or a zone ID.</param>
public sealed record TimeZoneChoice(string Id, string Name);

/// <summary>An entry in the "Theme" drop-down.</summary>
public sealed record ThemeChoice(AppTheme Theme, string Name);

/// <summary>
/// Settings > Preferences: how entraspect displays things. Changes are saved immediately. Applies the saved theme when
/// created, before the window shows, and again whenever the theme setting changes.
/// </summary>
public partial class PreferencesViewModel : ViewModelBase
{
    public const string ZoneUnavailableText = "The saved time zone isn't available on this computer; showing UTC.";

    private readonly SettingsState _settings;
    private readonly TimeDisplay _time;
    private readonly IUserInteraction _ui;
    private AppTheme? _appliedTheme;

    // Set while the selection is copied from the settings, so it isn't saved straight back.
    private bool _syncing;

    public PreferencesViewModel(SettingsState settings, TimeDisplay time, IUserInteraction ui)
    {
        _settings = settings;
        _time = time;
        _ui = ui;
        TimeZoneChoices =
        [
            new TimeZoneChoice(AppSettings.UtcTimeZone, "UTC"),
            new TimeZoneChoice(AppSettings.LocalTimeZone, $"Local time ({time.LocalZoneName})"),
            .. time.Zones.All.Select(z => new TimeZoneChoice(z.Id, z.DisplayName)),
        ];

        SyncFromSettings();
        settings.Changed += (_, _) => SyncFromSettings();
    }

    /// <summary>UTC, local time, then every zone sorted by UTC offset then name.</summary>
    public IReadOnlyList<TimeZoneChoice> TimeZoneChoices { get; }

    [ObservableProperty]
    public partial TimeZoneChoice? SelectedTimeZone { get; set; }

    public IReadOnlyList<ThemeChoice> ThemeChoices { get; } =
    [
        new ThemeChoice(AppTheme.System, "Use system setting"),
        new ThemeChoice(AppTheme.Light, "Light"),
        new ThemeChoice(AppTheme.Dark, "Dark"),
    ];

    [ObservableProperty]
    public partial ThemeChoice? SelectedTheme { get; set; }

    /// <summary>Shown when the saved time zone isn't known on this computer.</summary>
    [ObservableProperty]
    public partial string TimeZoneNotice { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ErrorText { get; set; } = string.Empty;

    partial void OnSelectedTimeZoneChanged(TimeZoneChoice? value)
    {
        if (_syncing || value is null)
        {
            return;
        }

        ErrorText = _settings.SetTimeZone(value.Id) ?? string.Empty;
    }

    partial void OnSelectedThemeChanged(ThemeChoice? value)
    {
        if (_syncing || value is null)
        {
            return;
        }

        ErrorText = _settings.SetTheme(value.Theme) ?? string.Empty;
    }

    private void SyncFromSettings()
    {
        _syncing = true;
        SelectedTimeZone = TimeZoneChoices.FirstOrDefault(c => c.Id == _time.EffectiveTimeZone);
        TimeZoneNotice = _time.SavedZoneUnavailable ? ZoneUnavailableText : string.Empty;
        SelectedTheme = ThemeChoices.First(c => c.Theme == _settings.Current.Theme);
        _syncing = false;

        if (_appliedTheme != _settings.Current.Theme)
        {
            _appliedTheme = _settings.Current.Theme;
            _ui.SetTheme(_settings.Current.Theme);
        }
    }
}
