using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Entraspect.Core;

namespace Entraspect.App.ViewModels;

/// <summary>
/// Overview page. Shows the last scan time and one line per check; spec 002 step 5 adds per-category counts and the
/// permission banners.
/// </summary>
public partial class OverviewViewModel : ViewModelBase
{
    public const string NoScanText = "No scan yet. Use Scan in the header to run every enabled check.";

    [ObservableProperty]
    public partial string LastScanText { get; set; } = NoScanText;

    public ObservableCollection<string> CheckLines { get; } = [];

    public void SetResults(IReadOnlyList<ICheck> checks, IReadOnlyList<CheckResult>? results, DateTimeOffset? scannedAt)
    {
        CheckLines.Clear();
        if (results is null || scannedAt is null)
        {
            LastScanText = NoScanText;
            return;
        }

        LastScanText = $"Last scan: {scannedAt:yyyy-MM-dd HH:mm} UTC";
        foreach (var check in checks)
        {
            var result = results.FirstOrDefault(r => r.CheckId == check.Id);
            CheckLines.Add($"{check.Title}: {Describe(result)}");
        }
    }

    private static string Describe(CheckResult? result) => result?.Status switch
    {
        null => "not run (disabled)",
        CheckResultStatus.Succeeded => $"{result.Findings.Count} finding(s)",
        CheckResultStatus.Failed => $"failed: {result.ErrorMessage}",
        CheckResultStatus.MissingPermissions => $"missing permissions: {string.Join(", ", result.MissingPermissions)}",
        CheckResultStatus.Cancelled => "cancelled",
        _ => string.Empty,
    };
}
