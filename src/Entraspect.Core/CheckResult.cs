using System.Collections.Generic;

namespace Entraspect.Core;

public enum CheckResultStatus
{
    Succeeded,
    Failed,
    MissingPermissions,

    /// <summary>Not run because the tenant doesn't have the licence the check needs.</summary>
    MissingLicence,
    Cancelled,
}

public sealed class CheckResult
{
    public required string CheckId { get; init; }

    public required CheckResultStatus Status { get; init; }

    public IReadOnlyList<Finding> Findings { get; init; } = System.Array.Empty<Finding>();

    public IReadOnlyList<string> MissingPermissions { get; init; } = System.Array.Empty<string>();

    public string? ErrorMessage { get; init; }

    /// <summary>HTTP status code from the directory service if the check failed on a read, otherwise null.</summary>
    public int? StatusCode { get; init; }

    /// <summary>Error code from the directory service if the check failed on a read and one was given.</summary>
    public string? ErrorCode { get; init; }

    public System.DateTimeOffset? CompletedAt { get; init; }

    /// <summary>When the check's source report was last refreshed, if the check reads one (see <see cref="ICheck.GetDataUpdatedAsync"/>).</summary>
    public System.DateTimeOffset? DataUpdatedAt { get; init; }
}
