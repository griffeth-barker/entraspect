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
    [NotifyPropertyChangedFor(nameof(ToolTip), nameof(ShowBadges))]
    public partial bool IsEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    /// <summary>Why the item is disabled, shown as its tooltip; null means it needs a connection.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolTip))]
    public partial string? DisabledReason { get; set; }

    public string? ToolTip => IsEnabled ? null : DisabledReason ?? ConnectFirstToolTip;

    /// <summary>The number of findings on the count badge; 0 means no count badge.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCount), nameof(ShowBadges))]
    public partial int BadgeCount { get; set; }

    /// <summary>The highest severity among the counted findings, or null when there is no count badge.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCritical), nameof(IsWarning), nameof(IsInfo))]
    public partial Severity? BadgeSeverity { get; set; }

    public bool IsCritical => BadgeSeverity == Severity.Critical;

    public bool IsWarning => BadgeSeverity == Severity.Warning;

    public bool IsInfo => BadgeSeverity == Severity.Info;

    /// <summary>The count by severity, most severe first, for example "1 critical, 3 warning".</summary>
    [ObservableProperty]
    public partial string? CountToolTip { get; set; }

    /// <summary>True when there is no result to count: the check failed or is missing permissions.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBadges))]
    public partial bool HasProblem { get; set; }

    /// <summary>Why there is no result, for example "Check failed".</summary>
    [ObservableProperty]
    public partial string? ProblemToolTip { get; set; }

    public bool HasCount => BadgeCount > 0;

    /// <summary>A disabled item (not connected, unlicensed) shows no badge.</summary>
    public bool ShowBadges => IsEnabled && (HasCount || HasProblem);

    // A check's own result, which a collapsed section adds up.
    private int _critical;
    private int _warning;
    private int _info;
    private CheckResultStatus? _problem;

    /// <summary>Shows or clears the problem badge on a page that isn't a check (Settings > Checks).</summary>
    public void SetWarning(bool warning)
    {
        HasProblem = warning;
        ProblemToolTip = warning ? MissingPermissionsWarning : null;
    }

    public const string FailedToolTip = "Check failed";

    public const string MissingPermissionsToolTip = "Missing permissions";

    public const string MissingPermissionsWarning = "A check is missing permissions";

    public NavItemViewModel Add(NavItemViewModel child)
    {
        child.Parent = this;
        Children.Add(child);
        return this;
    }

    /// <summary>
    /// Updates the badges from the last scan: the finding count when the check succeeded with findings, the problem
    /// badge when it failed or is missing permissions, and no badge when it found nothing, before the first scan, or
    /// when it was cancelled, not run or isn't licensed.
    /// </summary>
    public void SetResult(CheckResult? result)
    {
        var findings = result?.Status == CheckResultStatus.Succeeded ? result.Findings : [];
        _critical = findings.Count(f => f.Severity == Severity.Critical);
        _warning = findings.Count(f => f.Severity == Severity.Warning);
        _info = findings.Count(f => f.Severity == Severity.Info);
        _problem = result?.Status is CheckResultStatus.Failed or CheckResultStatus.MissingPermissions
            ? result.Status
            : null;

        ShowCount(_critical, _warning, _info);
        HasProblem = _problem is not null;
        ProblemToolTip = _problem switch
        {
            CheckResultStatus.Failed => FailedToolTip,
            CheckResultStatus.MissingPermissions => MissingPermissionsToolTip,
            _ => null,
        };

        Parent?.UpdateSummary();
    }

    // Each child shows its own badges, so only a collapsed section sums them up.
    partial void OnIsExpandedChanged(bool value) => UpdateSummary();

    /// <summary>
    /// A collapsed section shows its checks' findings as one count badge, and a problem badge when any of them has no
    /// result, so a failed check is never hidden. An expanded section shows no badge.
    /// </summary>
    private void UpdateSummary()
    {
        if (Kind != NavItemKind.Section)
        {
            return;
        }

        var children = IsExpanded ? [] : Children.ToList();
        ShowCount(children.Sum(c => c._critical), children.Sum(c => c._warning), children.Sum(c => c._info));

        var parts = new[]
        {
            Checks(children.Count(c => c._problem == CheckResultStatus.Failed), "failed"),
            Checks(children.Count(c => c._problem == CheckResultStatus.MissingPermissions), "missing permissions"),
        }.OfType<string>().ToList();
        HasProblem = parts.Count > 0;
        ProblemToolTip = HasProblem ? string.Join(", ", parts) : null;
    }

    private void ShowCount(int critical, int warning, int info)
    {
        BadgeCount = critical + warning + info;
        BadgeSeverity = critical > 0 ? Severity.Critical
            : warning > 0 ? Severity.Warning
            : info > 0 ? Severity.Info
            : null;

        var parts = new[] { Part(critical, "critical"), Part(warning, "warning"), Part(info, "info") }
            .OfType<string>()
            .ToList();
        CountToolTip = parts.Count > 0 ? string.Join(", ", parts) : null;
    }

    private static string? Part(int count, string severity) => count > 0 ? $"{count} {severity}" : null;

    private static string? Checks(int count, string what) =>
        count switch
        {
            0 => null,
            1 => $"1 check {what}",
            _ => $"{count} checks {what}",
        };
}
