using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Entraspect.Core;

namespace Entraspect.App.ViewModels;

/// <summary>Finding counts for one navigation section. Selecting it opens the section's first check.</summary>
public sealed record CategorySummary(string Title, int Critical, int Warning, int Info, NavItemViewModel Target);

/// <summary>A check that failed or is missing permissions in the last scan, with the reason.</summary>
public sealed record CheckProblem(string Title, string Reason, NavItemViewModel Target);

/// <summary>
/// Overview: the permission banners, the last scan time, Critical, Warning and Info counts per category, and the
/// checks that failed or are missing permissions.
/// </summary>
public partial class OverviewViewModel : ViewModelBase
{
    public const string NoScanText = "No scan yet. Use Scan in the header to run every enabled check.";

    private readonly Action<NavItemViewModel> _navigate;

    public OverviewViewModel(
        PermissionBannerViewModel banner,
        ExcessPermissionsViewModel excessPermissions,
        Action<NavItemViewModel> navigate)
    {
        Banner = banner;
        ExcessPermissions = excessPermissions;
        _navigate = navigate;
    }

    public PermissionBannerViewModel Banner { get; }

    public ExcessPermissionsViewModel ExcessPermissions { get; }

    [ObservableProperty]
    public partial bool HasScan { get; set; }

    [ObservableProperty]
    public partial string LastScanText { get; set; } = NoScanText;

    public ObservableCollection<CategorySummary> Categories { get; } = [];

    public ObservableCollection<CheckProblem> Problems { get; } = [];

    [ObservableProperty]
    public partial bool HasProblems { get; set; }

    /// <param name="sections">The navigation sections; each child is a check item with a <see cref="CheckPageViewModel"/>.</param>
    /// <param name="results">The last scan's results, or null before the first scan.</param>
    public void SetResults(
        IEnumerable<NavItemViewModel> sections,
        IReadOnlyList<CheckResult>? results,
        DateTimeOffset? scannedAt)
    {
        Categories.Clear();
        Problems.Clear();
        HasScan = results is not null && scannedAt is not null;
        LastScanText = HasScan ? $"Last scan: {scannedAt:yyyy-MM-dd HH:mm} UTC" : NoScanText;

        if (results is not null)
        {
            var byId = results.ToDictionary(r => r.CheckId);
            foreach (var section in sections)
            {
                var findings = new List<Finding>();
                foreach (var item in section.Children)
                {
                    if (item.Page is not CheckPageViewModel page || !byId.TryGetValue(page.Check.Id, out var result))
                    {
                        continue;
                    }

                    findings.AddRange(result.Findings);
                    if (Reason(result) is { } reason)
                    {
                        Problems.Add(new CheckProblem(page.Title, reason, item));
                    }
                }

                if (section.Children.Count > 0)
                {
                    Categories.Add(new CategorySummary(
                        section.Title,
                        findings.Count(f => f.Severity == Severity.Critical),
                        findings.Count(f => f.Severity == Severity.Warning),
                        findings.Count(f => f.Severity == Severity.Info),
                        section.Children[0]));
                }
            }
        }

        HasProblems = Problems.Count > 0;
    }

    [RelayCommand]
    private void Open(NavItemViewModel? target)
    {
        if (target is not null)
        {
            _navigate(target);
        }
    }

    private static string? Reason(CheckResult result) => result.Status switch
    {
        CheckResultStatus.Failed => $"Failed: {result.ErrorMessage}",
        CheckResultStatus.MissingPermissions =>
            $"Not run: missing permission(s) {string.Join(", ", result.MissingPermissions)}",
        _ => null,
    };
}
