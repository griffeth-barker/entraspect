using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Entraspect.Core;

namespace Entraspect.App.ViewModels;

/// <summary>
/// One reusable page for every check: the permission banner, the title (with the description in an info tip), the
/// status of the last run, and its findings with a severity and text filter. Required permissions are listed on
/// Settings > Checks, not here.
/// </summary>
public partial class CheckPageViewModel : ViewModelBase
{
    public const string NotRunText = "Not run yet.";
    public const string DisabledText = "Turned off in Settings > Checks.";

    private IReadOnlyList<FindingRow> _allRows = [];

    public CheckPageViewModel(ICheck check, PermissionBannerViewModel banner)
    {
        Check = check;
        Banner = banner;
    }

    public ICheck Check { get; }

    public PermissionBannerViewModel Banner { get; }

    public string Title => Check.Title;

    public string Description => Check.Description;

    [ObservableProperty]
    public partial bool IsDisabled { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = NotRunText;

    [ObservableProperty]
    public partial string ErrorText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ObservableCollection<FindingRow> Findings { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilterSummary))]
    public partial int TotalCount { get; set; }

    [ObservableProperty]
    public partial bool ShowCritical { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowWarning { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowInfo { get; set; } = true;

    [ObservableProperty]
    public partial string FilterText { get; set; } = string.Empty;

    public string FilterSummary => Findings.Count == TotalCount
        ? $"{TotalCount} finding(s)"
        : $"Showing {Findings.Count} of {TotalCount} finding(s)";

    partial void OnShowCriticalChanged(bool value) => ApplyFilter();

    partial void OnShowWarningChanged(bool value) => ApplyFilter();

    partial void OnShowInfoChanged(bool value) => ApplyFilter();

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    /// <summary>Shows the permission banner when any required permission isn't granted.</summary>
    /// <param name="granted">The connection's granted permissions, or null when not connected.</param>
    public void SetGrantedPermissions(IReadOnlyCollection<string>? granted)
    {
        Banner.ShowForCheck(granted is null
            ? []
            : Check.RequiredPermissions.Where(p => !granted.Contains(p)).ToList());
    }

    public void SetResult(CheckResult? result)
    {
        ErrorText = string.Empty;
        _allRows = [];

        switch (result?.Status)
        {
            case null:
                StatusText = NotRunText;
                break;
            case CheckResultStatus.Succeeded:
                // Default order: most severe first, then the soonest due date (no due date last).
                _allRows = result.Findings
                    .OrderByDescending(f => f.Severity)
                    .ThenBy(f => f.DueDate ?? DateTimeOffset.MaxValue)
                    .Select(f => new FindingRow(f))
                    .ToList();
                StatusText = $"Last run {result.CompletedAt:yyyy-MM-dd HH:mm} UTC";
                break;
            case CheckResultStatus.Failed:
                StatusText = $"Last run {result.CompletedAt:yyyy-MM-dd HH:mm} UTC failed";
                ErrorText = result.StatusCode == 403
                    ? $"{result.ErrorMessage}\n\nAccess was denied. The app registration is likely missing " +
                      $"{string.Join(", ", Check.RequiredPermissions)}, or admin consent has not been granted."
                    : result.ErrorMessage ?? "Unknown error.";
                break;
            case CheckResultStatus.MissingPermissions:
                // The permission banner already explains what is missing.
                StatusText = "Not run: missing permissions";
                break;
            case CheckResultStatus.Cancelled:
                StatusText = "Last scan cancelled";
                break;
        }

        TotalCount = _allRows.Count;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        Findings = new ObservableCollection<FindingRow>(_allRows.Where(row =>
            (row.Severity switch
            {
                Severity.Critical => ShowCritical,
                Severity.Warning => ShowWarning,
                _ => ShowInfo,
            })
            && row.Matches(FilterText)));
        OnPropertyChanged(nameof(FilterSummary));
    }
}
