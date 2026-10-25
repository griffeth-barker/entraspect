using System.Reflection;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Entraspect.App.Services;

namespace Entraspect.App.ViewModels;

/// <summary>
/// Settings > About: the logo, version and what entraspect is for, the excess permissions banner when connected, and the app
/// data folder with settings.json to edit in a text editor and reload. Required permissions are on Settings > Checks.
/// </summary>
public partial class AboutViewModel : ViewModelBase
{
    public const string Description =
        "entraspect is a read-only tool that scans a Microsoft Entra ID tenant for hygiene issues that are easy to " +
        "miss: expiring app secrets and certificates, users without MFA, empty or ownerless groups, and Conditional " +
        "Access policies that don't do what you expect. It shows what needs attention, so you can fix it in the " +
        "Entra admin center.";

    private readonly SettingsState _settings;
    private readonly TimeDisplay _time;
    private readonly IUserInteraction _ui;

    public AboutViewModel(
        ExcessPermissionsViewModel excessPermissions,
        string appDataFolder,
        SettingsState settings,
        TimeDisplay time,
        IUserInteraction ui)
    {
        ExcessPermissions = excessPermissions;
        AppDataFolder = appDataFolder;
        _settings = settings;
        _time = time;
        _ui = ui;
    }

    public string Version { get; } =
        typeof(AboutViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0]
        ?? typeof(AboutViewModel).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    public string DescriptionText => Description;

    public ExcessPermissionsViewModel ExcessPermissions { get; }

    public string AppDataFolder { get; }

    [ObservableProperty]
    public partial bool IsConnected { get; set; }

    /// <summary>Why settings.json couldn't be opened or reloaded.</summary>
    [ObservableProperty]
    public partial string SettingsErrorText { get; set; } = string.Empty;

    /// <summary>A short note after settings.json was reloaded.</summary>
    [ObservableProperty]
    public partial string SettingsStatusText { get; set; } = string.Empty;

    /// <summary>Opens settings.json in the default app for .json files, saving it first if it doesn't exist yet.</summary>
    [RelayCommand]
    private async Task OpenSettingsFileAsync()
    {
        SettingsStatusText = string.Empty;
        SettingsErrorText = await AppFile.OpenAsync(_ui, _settings.FilePath, _settings.Save) ?? string.Empty;
    }

    /// <summary>
    /// Reads settings.json again and applies it at once: checks, check options, time zone and theme. If the file can't
    /// be read or has an invalid value, shows why and keeps the current settings; the file isn't written.
    /// </summary>
    [RelayCommand]
    private void ReloadSettings()
    {
        SettingsStatusText = string.Empty;
        SettingsErrorText = _settings.Reload(s => _time.ValidateTimeZone(s.TimeZone)) ?? string.Empty;
        if (SettingsErrorText.Length == 0)
        {
            SettingsStatusText = "Reloaded settings.json.";
        }
    }
}
