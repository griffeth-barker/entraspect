using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Entraspect.Core;

namespace Entraspect.App.ViewModels;

/// <summary>
/// The missing-permission banner on a check page and on Overview. entraspect never adds or consents permissions
/// itself, since that would need a write permission: the banner links to the admin center, where the admin does it.
/// </summary>
public partial class PermissionBannerViewModel : ViewModelBase
{
    private readonly Func<Task> _openAdminCenter;
    private readonly Func<string, Task> _copyText;

    public PermissionBannerViewModel(Func<Task> openAdminCenter, Func<string, Task> copyText, IRelayCommand reconnect)
    {
        _openAdminCenter = openAdminCenter;
        _copyText = copyText;
        ReconnectCommand = reconnect;
    }

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    public partial string Message { get; set; } = string.Empty;

    /// <summary>One line per missing permission with the checks it blocks (Overview only).</summary>
    public ObservableCollection<string> Details { get; } = [];

    [ObservableProperty]
    public partial bool HasDetails { get; set; }

    public IReadOnlyList<string> MissingPermissions { get; private set; } = [];

    public IRelayCommand ReconnectCommand { get; }

    /// <summary>Shows the banner for one check, or hides it when nothing is missing.</summary>
    public void ShowForCheck(IReadOnlyList<string> missing)
    {
        MissingPermissions = missing;
        Details.Clear();
        HasDetails = false;
        Message = missing.Count == 0 ? string.Empty : CheckMessage(missing);
        IsOpen = missing.Count > 0;
    }

    /// <summary>Shows every missing permission with the checks it blocks, or hides the banner when none are missing.</summary>
    public void ShowForOverview(IReadOnlyList<PermissionReview.MissingPermission> missing)
    {
        MissingPermissions = missing.Select(m => m.Permission).ToList();
        Details.Clear();
        foreach (var permission in missing)
        {
            Details.Add($"{permission.Permission}: needed by {string.Join(", ", permission.Checks.Select(c => c.Title))}");
        }

        HasDetails = Details.Count > 0;
        Message = missing.Count == 0 ? string.Empty : OverviewMessage;
        IsOpen = missing.Count > 0;
    }

    public void Hide() => ShowForCheck([]);

    public const string OverviewMessage =
        "Some enabled checks need Microsoft Graph application permissions that aren't granted to this app " +
        "registration. Add them and grant admin consent, then reconnect.";

    public static string CheckMessage(IReadOnlyList<string> missing) => missing.Count == 1
        ? $"This check needs the Microsoft Graph application permission {missing[0]}, which isn't granted to this " +
          "app registration. Add it and grant admin consent, then reconnect."
        : $"This check needs the Microsoft Graph application permissions {string.Join(", ", missing)}, which aren't " +
          "granted to this app registration. Add them and grant admin consent, then reconnect.";

    [RelayCommand]
    private Task OpenAdminCenterAsync() => _openAdminCenter();

    [RelayCommand]
    private Task CopyPermissionNamesAsync() => _copyText(string.Join(Environment.NewLine, MissingPermissions));
}
