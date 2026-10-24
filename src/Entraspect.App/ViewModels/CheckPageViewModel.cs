using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Entraspect.Core;

namespace Entraspect.App.ViewModels;

/// <summary>
/// One reusable page for every check: its description, the status of the last run and its findings.
/// </summary>
public partial class CheckPageViewModel : ViewModelBase
{
    public const string NotRunText = "Not run yet. Use Scan in the header to run every enabled check.";

    public CheckPageViewModel(ICheck check)
    {
        Check = check;
    }

    public ICheck Check { get; }

    public string Title => Check.Title;

    public string Description => Check.Description;

    public string RequiredPermissionsText => string.Join(", ", Check.RequiredPermissions);

    [ObservableProperty]
    public partial string StatusText { get; set; } = NotRunText;

    [ObservableProperty]
    public partial string ErrorText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ObservableCollection<Finding> Findings { get; set; } = [];

    public void SetResult(CheckResult? result)
    {
        ErrorText = string.Empty;
        Findings = [];

        if (result is null)
        {
            StatusText = NotRunText;
            return;
        }

        switch (result.Status)
        {
            case CheckResultStatus.Succeeded:
                // Default order: most severe first, then the soonest due date (no due date last).
                Findings = new ObservableCollection<Finding>(result.Findings
                    .OrderByDescending(f => f.Severity)
                    .ThenBy(f => f.DueDate ?? DateTimeOffset.MaxValue));
                StatusText = $"Last run {result.CompletedAt:yyyy-MM-dd HH:mm} UTC: {result.Findings.Count} finding(s).";
                break;
            case CheckResultStatus.Failed:
                StatusText = "The last run failed.";
                ErrorText = result.StatusCode == 403
                    ? $"{result.ErrorMessage}\n\nThe app registration is likely missing {RequiredPermissionsText}, or admin consent has not been granted."
                    : result.ErrorMessage ?? "Unknown error.";
                break;
            case CheckResultStatus.MissingPermissions:
                StatusText = "Not run: missing permissions.";
                ErrorText = $"Missing Microsoft Graph application permission(s): {string.Join(", ", result.MissingPermissions)}.";
                break;
            case CheckResultStatus.Cancelled:
                StatusText = "The last scan was cancelled before this check finished.";
                break;
        }
    }
}
