using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Entraspect.Core;

namespace Entraspect.App.ViewModels;

public enum NavItemKind
{
    Overview,
    Section,
    Check,
    Settings,
    SettingsPage,
}

/// <summary>
/// One entry in the navigation panel. Sections (a check category, or Settings) only group their children and are
/// never selected themselves. Not a <see cref="ViewModelBase"/>, so the view locator never builds a view for it.
/// </summary>
public partial class NavItemViewModel : ObservableObject
{
    public const string ConnectFirstToolTip = "Connect to a tenant first";

    public NavItemViewModel(NavItemKind kind, string title, string? icon = null, ViewModelBase? page = null)
    {
        Kind = kind;
        Title = title;
        Icon = icon;
        Page = page;
    }

    public NavItemKind Kind { get; }

    public string Title { get; }

    /// <summary>Name of a FluentAvalonia symbol, or null for no icon.</summary>
    public string? Icon { get; }

    /// <summary>The page shown when this item is selected; null for sections.</summary>
    public ViewModelBase? Page { get; }

    public NavItemViewModel? Parent { get; private set; }

    public ObservableCollection<NavItemViewModel> Children { get; } = [];

    public bool IsPage => Page is not null;

    /// <summary>Pages that need a connection: Overview and the check pages.</summary>
    public bool RequiresConnection => Kind is NavItemKind.Overview or NavItemKind.Check;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolTip))]
    public partial bool IsEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public string? ToolTip => IsEnabled ? null : ConnectFirstToolTip;

    /// <summary>The finding count, or -1 when the badge shows the warning marker instead.</summary>
    [ObservableProperty]
    public partial int BadgeValue { get; set; } = -1;

    [ObservableProperty]
    public partial bool HasBadge { get; set; }

    [ObservableProperty]
    public partial bool HasWarning { get; set; }

    /// <summary>Shows or clears the warning marker on a page that isn't a check (Settings > Checks).</summary>
    public void SetWarning(bool warning)
    {
        BadgeValue = -1;
        HasWarning = warning;
        HasBadge = warning;
    }

    public NavItemViewModel Add(NavItemViewModel child)
    {
        child.Parent = this;
        Children.Add(child);
        return this;
    }

    /// <summary>
    /// Updates the badge from the last scan: the finding count when the check succeeded, a warning marker when it
    /// failed or is missing permissions, and no badge before the first scan or when it was cancelled or not run.
    /// </summary>
    public void SetResult(CheckResult? result)
    {
        switch (result?.Status)
        {
            case CheckResultStatus.Succeeded:
                BadgeValue = result.Findings.Count;
                HasWarning = false;
                HasBadge = true;
                break;
            case CheckResultStatus.Failed or CheckResultStatus.MissingPermissions:
                BadgeValue = -1;
                HasWarning = true;
                HasBadge = true;
                break;
            default:
                BadgeValue = -1;
                HasWarning = false;
                HasBadge = false;
                break;
        }
    }
}
