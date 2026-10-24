using CommunityToolkit.Mvvm.ComponentModel;
using Entraspect.App.Services;

namespace Entraspect.App.ViewModels;

/// <summary>Settings > Preferences: how entraspect displays things. Changes are saved immediately.</summary>
public partial class PreferencesViewModel : ViewModelBase
{
    private readonly SettingsState _settings;

    public PreferencesViewModel(SettingsState settings, TimeDisplay time)
    {
        _settings = settings;
        LocalTimeLabel = $"Local time ({time.LocalZoneName})";
        UseLocalTime = settings.Current.UseLocalTime;
    }

    public string LocalTimeLabel { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseUtc))]
    public partial bool UseLocalTime { get; set; }

    /// <summary>The other radio button; setting it true switches to UTC.</summary>
    public bool UseUtc
    {
        get => !UseLocalTime;
        set => UseLocalTime = !value;
    }

    [ObservableProperty]
    public partial string ErrorText { get; set; } = string.Empty;

    partial void OnUseLocalTimeChanged(bool value) => ErrorText = _settings.SetUseLocalTime(value) ?? string.Empty;
}
