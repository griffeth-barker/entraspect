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
    private IReadOnlyList<string>? _groupIdsWithLicenseErrors;
    private IReadOnlyList<EnterpriseApp>? _enterpriseApps;
    private IReadOnlyList<UserRegistration>? _userRegistrations;
    private IReadOnlyList<AppPermissionGrant>? _graphApplicationPermissionGrants;
    private IReadOnlyList<ConditionalAccessPolicy>? _conditionalAccessPolicies;
    private IReadOnlyList<DirectoryUser>? _users;
    private IReadOnlyList<DirectoryRoleAssignment>? _directoryRoleAssignments;
    private IReadOnlyList<GuestSignInActivity>? _guestSignInActivity;
    private IReadOnlyList<DirectoryRoleDefinition>? _directoryRoleDefinitions;
    private bool? _securityDefaultsEnabled;
    private (DateTimeOffset Since, IReadOnlyDictionary<string, SingleFactorSignIns> SignIns)? _singleFactorSignIns;
    private (DateTimeOffset Since, ConditionalAccessUsage Usage)? _conditionalAccessUsage;
    private AuthorizationPolicySettings? _authorizationPolicy;
    private CrossTenantAccessSettings? _crossTenantAccessDefaults;
    private IReadOnlyList<CrossTenantAccessSettings>? _crossTenantAccessPartners;
    private readonly Dictionary<string, IReadOnlyList<string>> _groupTransitiveUserIds =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PerUserMfaState> _perUserMfaStates = new(StringComparer.OrdinalIgnoreCase);

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

    public async Task<IReadOnlyList<string>> GetGroupIdsWithLicenseErrorsAsync(
        CancellationToken cancellationToken = default)
    {
        return _groupIdsWithLicenseErrors ??=
            await _inner.GetGroupIdsWithLicenseErrorsAsync(cancellationToken).ConfigureAwait(false);
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
            var innerProgress = progress is null ? null : new SyncProgress<int>(n => progress.Report(known + n));
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

    public async Task<IReadOnlyList<DirectoryUser>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        return _users ??= await _inner.GetUsersAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DirectoryRoleAssignment>> GetDirectoryRoleAssignmentsAsync(
        CancellationToken cancellationToken = default)
    {
        return _directoryRoleAssignments ??=
            await _inner.GetDirectoryRoleAssignmentsAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<GuestSignInActivity>> GetGuestSignInActivityAsync(
        CancellationToken cancellationToken = default)
    {
        return _guestSignInActivity ??=
            await _inner.GetGuestSignInActivityAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DirectoryRoleDefinition>> GetDirectoryRoleDefinitionsAsync(
        CancellationToken cancellationToken = default)
    {
        return _directoryRoleDefinitions ??=
            await _inner.GetDirectoryRoleDefinitionsAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads each group's members at most once per scan.</summary>
    public async Task<IReadOnlyList<string>> GetGroupTransitiveUserIdsAsync(
        string groupId,
        CancellationToken cancellationToken = default)
    {
        if (!_groupTransitiveUserIds.TryGetValue(groupId, out var userIds))
        {
            userIds = await _inner.GetGroupTransitiveUserIdsAsync(groupId, cancellationToken).ConfigureAwait(false);
            _groupTransitiveUserIds[groupId] = userIds;
        }

        return userIds;
    }

    public async Task<bool> GetSecurityDefaultsEnabledAsync(CancellationToken cancellationToken = default)
    {
        return _securityDefaultsEnabled ??=
            await _inner.GetSecurityDefaultsEnabledAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads each user's state at most once per scan.</summary>
    public async Task<PerUserMfaState> GetPerUserMfaStateAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (!_perUserMfaStates.TryGetValue(userId, out var state))
        {
            state = await _inner.GetPerUserMfaStateAsync(userId, cancellationToken).ConfigureAwait(false);
            _perUserMfaStates[userId] = state;
        }

        return state;
    }

    /// <summary>Kept for the last <paramref name="since"/> asked for; a different start reads again.</summary>
    public async Task<IReadOnlyDictionary<string, SingleFactorSignIns>> GetSingleFactorSignInsAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        if (_singleFactorSignIns is { } cached && cached.Since == since)
        {
            return cached.SignIns;
        }

        var signIns = await _inner.GetSingleFactorSignInsAsync(since, cancellationToken).ConfigureAwait(false);
        _singleFactorSignIns = (since, signIns);
        return signIns;
    }

    /// <summary>
    /// Kept for the last <paramref name="since"/> asked for; a different start reads again. A cached read reports its
    /// total once.
    /// </summary>
    public async Task<ConditionalAccessUsage> GetConditionalAccessUsageAsync(
        DateTimeOffset since,
        IProgress<int>? signInsRead = null,
        CancellationToken cancellationToken = default)
    {
        if (_conditionalAccessUsage is { } cached && cached.Since == since)
        {
            signInsRead?.Report(cached.Usage.SignInsRead);
            return cached.Usage;
        }

        var usage = await _inner.GetConditionalAccessUsageAsync(since, signInsRead, cancellationToken)
            .ConfigureAwait(false);
        _conditionalAccessUsage = (since, usage);
        return usage;
    }

    public async Task<AuthorizationPolicySettings> GetAuthorizationPolicyAsync(
        CancellationToken cancellationToken = default)
    {
        return _authorizationPolicy ??=
            await _inner.GetAuthorizationPolicyAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<CrossTenantAccessSettings> GetCrossTenantAccessDefaultsAsync(
        CancellationToken cancellationToken = default)
    {
        return _crossTenantAccessDefaults ??=
            await _inner.GetCrossTenantAccessDefaultsAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CrossTenantAccessSettings>> GetCrossTenantAccessPartnersAsync(
        CancellationToken cancellationToken = default)
    {
        return _crossTenantAccessPartners ??=
            await _inner.GetCrossTenantAccessPartnersAsync(cancellationToken).ConfigureAwait(false);
    }
}
