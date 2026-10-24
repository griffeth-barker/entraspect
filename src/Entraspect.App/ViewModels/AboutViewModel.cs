using System.Collections.Generic;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using Entraspect.Core;

namespace Entraspect.App.ViewModels;

/// <summary>A Microsoft Graph application permission some check needs, with the checks that need it.</summary>
public sealed record RequiredPermission(string Permission, string UsedBy);

/// <summary>
/// Settings > About: version, the read-only statement, every application permission any check needs, the excess
/// permissions banner when connected, and the app data folder.
/// </summary>
public partial class AboutViewModel : ViewModelBase
{
    public const string ReadOnlyStatement =
        "entraspect is read-only. It only uses Microsoft Graph read permissions and never changes your tenant: it " +
        "doesn't add or consent permissions, and doesn't create, update or delete anything.";

    public AboutViewModel(IReadOnlyList<ICheck> checks, ExcessPermissionsViewModel excessPermissions, string appDataFolder)
    {
        ExcessPermissions = excessPermissions;
        AppDataFolder = appDataFolder;
        RequiredPermissions = checks
            .SelectMany(c => c.RequiredPermissions.Select(p => (Permission: p, Check: c)))
            .GroupBy(x => x.Permission, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new RequiredPermission(g.Key, string.Join(", ", g.Select(x => x.Check.Title).Distinct())))
            .ToList();
    }

    public string Version { get; } =
        typeof(AboutViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0]
        ?? typeof(AboutViewModel).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    public string ReadOnlyText => ReadOnlyStatement;

    public IReadOnlyList<RequiredPermission> RequiredPermissions { get; }

    public ExcessPermissionsViewModel ExcessPermissions { get; }

    public string AppDataFolder { get; }

    [ObservableProperty]
    public partial bool IsConnected { get; set; }
}
