using System.Collections.Generic;
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
        var total = _checks.Count;
        var runPosition = 0;

        for (var i = 0; i < total; i++)
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

            runPosition++;
            _progress?.Report(new ScanProgress(runPosition, total, check.Title));

            try
            {
                var findings = await check.RunAsync(_reader, _options, cancellationToken).ConfigureAwait(false);
                results.Add(new CheckResult
                {
                    CheckId = check.Id,
                    Status = CheckResultStatus.Succeeded,
                    Findings = findings,
                    CompletedAt = _timeProvider.GetUtcNow(),
                });
            }
            catch (OperationCanceledException)
            {
                results.Add(MakeCancelled(check));
                for (var j = i + 1; j < total; j++)
                {
                    results.Add(MakeCancelled(_checks[j]));
                }

                return results;
            }
            catch (DirectoryReadException ex)
            {
                results.Add(new CheckResult
                {
                    CheckId = check.Id,
                    Status = CheckResultStatus.Failed,
                    ErrorMessage = ex.Message,
                    StatusCode = ex.StatusCode,
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

    private static CheckResult MakeCancelled(ICheck check) => new()
    {
        CheckId = check.Id,
        Status = CheckResultStatus.Cancelled,
    };
}
