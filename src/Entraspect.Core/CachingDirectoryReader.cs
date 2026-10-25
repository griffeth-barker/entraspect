using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Entraspect.Core;

/// <summary>
/// An <see cref="IDirectoryReader"/> decorator that fetches each collection at most once per scan, so two checks that
/// read the same data cause one directory read. A new instance is created for each scan. A failed read is not cached.
/// Every later spec that adds an <see cref="IDirectoryReader"/> method must add it here too.
/// </summary>
public sealed class CachingDirectoryReader : IDirectoryReader
{
    private readonly IDirectoryReader _inner;
    private IReadOnlyList<AppRegistration>? _appRegistrations;
    private IReadOnlyList<DirectoryGroup>? _groups;
    private IReadOnlyList<EnterpriseApp>? _enterpriseApps;
    private IReadOnlyList<UserRegistration>? _userRegistrations;
    private IReadOnlyList<AppPermissionGrant>? _graphApplicationPermissionGrants;
    private IReadOnlyList<ConditionalAccessPolicy>? _conditionalAccessPolicies;

    // Each user ID looked up so far, and whether it was missing.
    private readonly Dictionary<string, bool> _userIdMissing = new(StringComparer.OrdinalIgnoreCase);

    public CachingDirectoryReader(IDirectoryReader inner) => _inner = inner;

    public async Task<IReadOnlyList<AppRegistration>> GetAppRegistrationsAsync(
        CancellationToken cancellationToken = default)
    {
        return _appRegistrations ??=
            await _inner.GetAppRegistrationsAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DirectoryGroup>> GetGroupsAsync(CancellationToken cancellationToken = default)
    {
        return _groups ??= await _inner.GetGroupsAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<EnterpriseApp>> GetEnterpriseAppsAsync(
        CancellationToken cancellationToken = default)
    {
        return _enterpriseApps ??=
            await _inner.GetEnterpriseAppsAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<UserRegistration>> GetUserRegistrationsAsync(
        CancellationToken cancellationToken = default)
    {
        return _userRegistrations ??=
            await _inner.GetUserRegistrationsAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AppPermissionGrant>> GetGraphApplicationPermissionGrantsAsync(
        CancellationToken cancellationToken = default)
    {
        return _graphApplicationPermissionGrants ??=
            await _inner.GetGraphApplicationPermissionGrantsAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ConditionalAccessPolicy>> GetConditionalAccessPoliciesAsync(
        CancellationToken cancellationToken = default)
    {
        return _conditionalAccessPolicies ??=
            await _inner.GetConditionalAccessPoliciesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Looks up only the IDs no earlier call looked up. Progress counts the IDs already known as looked up.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetMissingUserIdsAsync(
        IReadOnlyCollection<string> userIds,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var distinct = userIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var unknown = distinct.Where(id => !_userIdMissing.ContainsKey(id)).ToList();

        if (unknown.Count > 0)
        {
            var known = distinct.Count - unknown.Count;
            var innerProgress = progress is null ? null : new OffsetProgress(progress, known);
            var missing = await _inner.GetMissingUserIdsAsync(unknown, innerProgress, cancellationToken)
                .ConfigureAwait(false);
            var missingSet = missing.ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var id in unknown)
            {
                _userIdMissing[id] = missingSet.Contains(id);
            }
        }

        return distinct.Where(id => _userIdMissing[id]).ToList();
    }

    private sealed class OffsetProgress(IProgress<int> target, int offset) : IProgress<int>
    {
        public void Report(int value) => target.Report(offset + value);
    }
}
