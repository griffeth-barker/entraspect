using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Entraspect.App.ViewModels;

/// <summary>
/// Settings > About: version, the read-only statement, the excess permissions banner when connected, and the app
/// data folder. Required permissions are on Settings > Checks.
/// </summary>
public partial class AboutViewModel : ViewModelBase
{
    public const string ReadOnlyStatement =
        "entraspect is read-only. It only uses Microsoft Graph read permissions and never changes your tenant: it " +
        "doesn't add or consent permissions, and doesn't create, update or delete anything.";

    public AboutViewModel(ExcessPermissionsViewModel excessPermissions, string appDataFolder)
    {
        ExcessPermissions = excessPermissions;
        AppDataFolder = appDataFolder;
    }

    public string Version { get; } =
        typeof(AboutViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0]
        ?? typeof(AboutViewModel).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    public string ReadOnlyText => ReadOnlyStatement;

    public ExcessPermissionsViewModel ExcessPermissions { get; }

    public string AppDataFolder { get; }

    [ObservableProperty]
    public partial bool IsConnected { get; set; }
}
