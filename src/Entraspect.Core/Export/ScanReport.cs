using System.Collections.Generic;

namespace Entraspect.Core;

/// <summary>
/// What an export contains: when the scan ran (UTC), which profile and tenant it ran against, the app version, and
/// each check's result and findings. It never holds credentials: no client secret, certificate, password or token.
/// </summary>
public sealed record ScanReport
{
    public required DateTimeOffset ScannedAt { get; init; }

    public required string ProfileName { get; init; }

    public required string TenantId { get; init; }

    public required string AppVersion { get; init; }

    public required IReadOnlyList<ScanReportCheck> Checks { get; init; }

    /// <summary>
    /// A report of <paramref name="results"/> in the order of <paramref name="checks"/>. A check without a result, for
    /// example one turned off, is left out.
    /// </summary>
    public static ScanReport Create(
        DateTimeOffset scannedAt,
        string profileName,
        string tenantId,
        string appVersion,
        IEnumerable<ICheck> checks,
        IEnumerable<CheckResult> results)
    {
        var byId = new Dictionary<string, CheckResult>(StringComparer.Ordinal);
        foreach (var result in results)
        {
            byId[result.CheckId] = result;
        }

        return new ScanReport
        {
            ScannedAt = scannedAt.ToUniversalTime(),
            ProfileName = profileName,
            TenantId = tenantId,
            AppVersion = appVersion,
            Checks = checks
                .Where(c => byId.ContainsKey(c.Id))
                .Select(c => ScanReportCheck.From(c, byId[c.Id]))
                .ToList(),
        };
    }
}

/// <summary>One check in a <see cref="ScanReport"/>: its status, why it failed or didn't run, and its findings.</summary>
public sealed record ScanReportCheck
{
    public required string CheckId { get; init; }

    public required string Title { get; init; }

    public required CheckCategory Category { get; init; }

    public required CheckResultStatus Status { get; init; }

    /// <summary>Why the check failed, as shown on its page; null unless <see cref="Status"/> is Failed.</summary>
    public string? Error { get; init; }

    /// <summary>HTTP status code from the directory service if the check failed on a read.</summary>
    public int? StatusCode { get; init; }

    /// <summary>Error code from the directory service if the check failed on a read and one was given.</summary>
    public string? ErrorCode { get; init; }

    public IReadOnlyList<string> MissingPermissions { get; init; } = [];

    public DateTimeOffset? CompletedAt { get; init; }

    public IReadOnlyList<Finding> Findings { get; init; } = [];

    /// <param name="findings">
    /// The findings to export, for example only those a page shows after filtering. Defaults to all of the result's.
    /// </param>
    public static ScanReportCheck From(ICheck check, CheckResult result, IReadOnlyList<Finding>? findings = null) => new()
    {
        CheckId = check.Id,
        Title = check.Title,
        Category = check.Category,
        Status = result.Status,
        Error = result.Status == CheckResultStatus.Failed ? CheckFailureMessage.For(result) : null,
        StatusCode = result.StatusCode,
        ErrorCode = result.ErrorCode,
        MissingPermissions = result.MissingPermissions,
        CompletedAt = result.CompletedAt?.ToUniversalTime(),
        Findings = findings ?? result.Findings,
    };
}
