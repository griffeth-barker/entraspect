using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Entraspect.Core;

/// <summary>
/// Runs the enabled checks one after another, in catalog order, returning one <see cref="CheckResult"/> per check.
/// </summary>
public sealed class ScanRunner
{
    private readonly IDirectoryReader _reader;
    private readonly IReadOnlyList<ICheck> _checks;
    private readonly ISet<string> _grantedPermissions;
    private readonly CheckOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly IProgress<ScanProgress>? _progress;

    public ScanRunner(
        IDirectoryReader reader,
        IReadOnlyList<ICheck> checks,
        IEnumerable<string> grantedPermissions,
        CheckOptions options,
        TimeProvider timeProvider,
        IProgress<ScanProgress>? progress = null)
    {
        _reader = reader;
        _checks = checks;
        _grantedPermissions = new HashSet<string>(grantedPermissions, StringComparer.Ordinal);
        _options = options;
        _timeProvider = timeProvider;
        _progress = progress;
    }

    public async Task<IReadOnlyList<CheckResult>> RunAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<CheckResult>(_checks.Count);
        var total = _checks.Count(check => MissingPermissionsFor(check).Count == 0);
        var runPosition = 0;

        for (var i = 0; i < _checks.Count; i++)
        {
            var check = _checks[i];
            var missing = MissingPermissionsFor(check);

            if (missing.Count > 0)
            {
                results.Add(new CheckResult
                {
                    CheckId = check.Id,
                    Status = CheckResultStatus.MissingPermissions,
                    MissingPermissions = missing,
                    CompletedAt = _timeProvider.GetUtcNow(),
                });
                continue;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                AddCancelledFrom(results, i);
                return results;
            }

            runPosition++;
            _progress?.Report(new ScanProgress(runPosition, total, check.Title));

            try
            {
                var findings = await check.RunAsync(_reader, _options, cancellationToken).ConfigureAwait(false);
                var dataUpdatedAt = await check.GetDataUpdatedAsync(_reader, cancellationToken).ConfigureAwait(false);
                results.Add(new CheckResult
                {
                    CheckId = check.Id,
                    Status = CheckResultStatus.Succeeded,
                    Findings = findings,
                    CompletedAt = _timeProvider.GetUtcNow(),
                    DataUpdatedAt = dataUpdatedAt,
                });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                AddCancelledFrom(results, i);
                return results;
            }
            catch (OperationCanceledException)
            {
                // Not requested by the caller, for example an HTTP timeout: only this check fails.
                results.Add(new CheckResult
                {
                    CheckId = check.Id,
                    Status = CheckResultStatus.Failed,
                    ErrorMessage = "The request timed out or was cancelled by the directory service.",
                    CompletedAt = _timeProvider.GetUtcNow(),
                });
            }
            catch (DirectoryReadException ex)
            {
                results.Add(new CheckResult
                {
                    CheckId = check.Id,
                    Status = CheckResultStatus.Failed,
                    ErrorMessage = ex.Message,
                    StatusCode = ex.StatusCode,
                    ErrorCode = ex.ErrorCode,
                    CompletedAt = _timeProvider.GetUtcNow(),
                });
            }
        }

        return results;
    }

    private List<string> MissingPermissionsFor(ICheck check)
    {
        var missing = new List<string>();
        foreach (var permission in check.RequiredPermissions)
        {
            if (!_grantedPermissions.Contains(permission))
            {
                missing.Add(permission);
            }
        }

        return missing;
    }

    /// <summary>Marks the check at <paramref name="start"/> and every later check as cancelled.</summary>
    private void AddCancelledFrom(List<CheckResult> results, int start)
    {
        for (var j = start; j < _checks.Count; j++)
        {
            var check = _checks[j];
            var missing = MissingPermissionsFor(check);
            results.Add(missing.Count > 0
                ? new CheckResult
                {
                    CheckId = check.Id,
                    Status = CheckResultStatus.MissingPermissions,
                    MissingPermissions = missing,
                    CompletedAt = _timeProvider.GetUtcNow(),
                }
                : new CheckResult { CheckId = check.Id, Status = CheckResultStatus.Cancelled });
        }
    }
}
