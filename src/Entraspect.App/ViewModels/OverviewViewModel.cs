using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Entraspect.Core;

namespace Entraspect.App.ViewModels;

/// <summary>
/// Finding counts for one navigation section. Selecting it opens the section's first available check. A section
/// whose checks all need a licence the tenant lacks is unavailable: its counts show as a dash on gray, so they don't
/// read as "nothing found".
/// </summary>
public sealed record CategorySummary(
    string Title,
    int Critical,
    int Warning,
    int Info,
    NavItemViewModel Target,
    string? UnavailableReason = null)
{
    public bool IsAvailable => UnavailableReason is null;

    public string CriticalText => Count(Critical);

    public string WarningText => Count(Warning);

    public string InfoText => Count(Info);

    private string Count(int value) => IsAvailable ? value.ToString() : "\u2013";
}

/// <summary>A check that failed or is missing permissions in the last scan, with the reason.</summary>
public sealed record CheckProblem(string Title, string Reason, NavItemViewModel Target);

/// <summary>
/// Overview: the permission banners, the last scan time, Critical, Warning and Info counts per category, and the
/// checks that failed or are missing permissions.
/// </summary>
public partial class OverviewViewModel : ViewModelBase
{
    public const string NoScanText = "No scan yet.";

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
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    public partial string ProfileName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TenantId { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ClientId { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ConnectedAtText { get; set; } = string.Empty;

    /// <summary>Shows the connected tenant at the top of the page, or clears it when disconnected.</summary>
    /// <param name="connectedAtText">The connect time, already formatted for the time preference.</param>
    public void SetConnection(Graph.ConnectionProfile? profile, string? connectedAtText)
    {
        IsConnected = profile is not null;
        ProfileName = profile?.Name ?? string.Empty;
        TenantId = profile?.TenantId ?? string.Empty;
        ClientId = profile?.ClientId ?? string.Empty;
        ConnectedAtText = connectedAtText ?? string.Empty;
    }

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
        string? scannedAtText)
    {
        Categories.Clear();
        Problems.Clear();
        HasScan = results is not null && scannedAtText is not null;
        LastScanText = HasScan ? $"Last scan: {scannedAtText}" : NoScanText;

        if (results is not null)
        {
            var byId = results.ToDictionary(r => r.CheckId);
            foreach (var section in sections)
            {
                var findings = new List<Finding>();
                var ran = false;
                string? unlicensedReason = null;
                foreach (var item in section.Children)
                {
                    if (item.Page is not CheckPageViewModel page || !byId.TryGetValue(page.Check.Id, out var result))
                    {
                        continue;
                    }

                    if (result.Status == CheckResultStatus.MissingLicence)
                    {
                        unlicensedReason ??= Licensing.Needs(page.Check.RequiredLicence);
                    }
                    else
                    {
                        ran = true;
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
                        section.Children.FirstOrDefault(c => c.IsEnabled) ?? section.Children[0],
                        ran ? null : unlicensedReason));
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
        CheckResultStatus.Failed => $"Failed: {CheckFailureMessage.For(result)}",
        CheckResultStatus.MissingPermissions =>
            $"Not run: missing permission(s) {string.Join(", ", result.MissingPermissions)}",
        _ => null,
    };
}
