namespace Entraspect.Core.Tests;

internal sealed class FakeDirectoryReader : IDirectoryReader
{
    private readonly IReadOnlyList<AppRegistration> _apps;
    private readonly IReadOnlyList<DirectoryGroup> _groups;
    private readonly IReadOnlyList<EnterpriseApp> _enterpriseApps;
    private readonly IReadOnlyList<UserRegistration> _userRegistrations;
    private readonly IReadOnlyList<AppPermissionGrant> _grants;
    private readonly IReadOnlyList<ConditionalAccessPolicy> _policies;
    private readonly HashSet<string> _existingUserIds;

    public FakeDirectoryReader(
        IReadOnlyList<AppRegistration> apps,
        IReadOnlyList<DirectoryGroup>? groups = null,
        IReadOnlyList<EnterpriseApp>? enterpriseApps = null,
        IReadOnlyList<UserRegistration>? userRegistrations = null,
        IReadOnlyList<AppPermissionGrant>? grants = null,
        IReadOnlyList<ConditionalAccessPolicy>? policies = null,
        IEnumerable<string>? existingUserIds = null)
    {
        _apps = apps;
        _groups = groups ?? [];
        _enterpriseApps = enterpriseApps ?? [];
        _userRegistrations = userRegistrations ?? [];
        _grants = grants ?? [];
        _policies = policies ?? [];
        _existingUserIds = new HashSet<string>(existingUserIds ?? [], StringComparer.OrdinalIgnoreCase);
    }

    public FakeDirectoryReader(IReadOnlyList<DirectoryGroup> groups)
        : this([], groups)
    {
    }

    public FakeDirectoryReader(IReadOnlyList<EnterpriseApp> enterpriseApps)
        : this([], enterpriseApps: enterpriseApps)
    {
    }

    public FakeDirectoryReader(IReadOnlyList<UserRegistration> userRegistrations)
        : this([], userRegistrations: userRegistrations)
    {
    }

    public Task<IReadOnlyList<AppRegistration>> GetAppRegistrationsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_apps);

    public Task<IReadOnlyList<DirectoryGroup>> GetGroupsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_groups);

    /// <summary>What <see cref="GetGroupIdsWithLicenseErrorsAsync"/> returns.</summary>
    public IReadOnlyList<string> GroupIdsWithLicenseErrors { get; init; } = [];

    /// <summary>When set, <see cref="GetGroupIdsWithLicenseErrorsAsync"/> throws it.</summary>
    public DirectoryReadException? LicenseErrorsFailure { get; init; }

    public Task<IReadOnlyList<string>> GetGroupIdsWithLicenseErrorsAsync(CancellationToken cancellationToken = default) =>
        LicenseErrorsFailure is { } failure ? throw failure : Task.FromResult(GroupIdsWithLicenseErrors);

    public Task<IReadOnlyList<EnterpriseApp>> GetEnterpriseAppsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_enterpriseApps);

    public Task<IReadOnlyList<UserRegistration>> GetUserRegistrationsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_userRegistrations);

    public Task<IReadOnlyList<AppPermissionGrant>> GetGraphApplicationPermissionGrantsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_grants);

    /// <summary>The user IDs passed to each <see cref="GetMissingUserIdsAsync"/> call.</summary>
    public List<IReadOnlyCollection<string>> UserLookups { get; } = [];

    public Task<IReadOnlyList<ConditionalAccessPolicy>> GetConditionalAccessPoliciesAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_policies);

    /// <summary>Every ID not in the existing user IDs given to the constructor is missing.</summary>
    public Task<IReadOnlyList<string>> GetMissingUserIdsAsync(
        IReadOnlyCollection<string> userIds,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        UserLookups.Add(userIds.ToList());
        var missing = new List<string>();
        var done = 0;

        foreach (var id in userIds)
        {
            if (!_existingUserIds.Contains(id))
            {
                missing.Add(id);
            }

            progress?.Report(++done);
        }

        return Task.FromResult<IReadOnlyList<string>>(missing);
    }

    public IReadOnlyList<DirectoryUser> Users { get; init; } = [];

    public IReadOnlyList<DirectoryRoleAssignment> RoleAssignments { get; init; } = [];

    /// <summary>Transitive user members by group ID. Any other ID has no members.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> GroupMembers { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>();

    public bool SecurityDefaultsEnabled { get; init; }

    /// <summary>Per-user MFA state by user ID. Any other user is <see cref="PerUserMfaState.Disabled"/>.</summary>
    public IReadOnlyDictionary<string, PerUserMfaState> PerUserMfaStates { get; init; } =
        new Dictionary<string, PerUserMfaState>();

    public IReadOnlyDictionary<string, SingleFactorSignIns> SignIns { get; init; } =
        new Dictionary<string, SingleFactorSignIns>();

    /// <summary>The group IDs passed to each <see cref="GetGroupTransitiveUserIdsAsync"/> call.</summary>
    public List<string> GroupMemberReads { get; } = [];

    /// <summary>The user IDs passed to each <see cref="GetPerUserMfaStateAsync"/> call.</summary>
    public List<string> PerUserMfaReads { get; } = [];

    /// <summary>The start passed to each <see cref="GetSingleFactorSignInsAsync"/> call.</summary>
    public List<DateTimeOffset> SignInReads { get; } = [];

    public Task<IReadOnlyList<DirectoryUser>> GetUsersAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Users);

    public Task<IReadOnlyList<DirectoryRoleAssignment>> GetDirectoryRoleAssignmentsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(RoleAssignments);

    public Task<IReadOnlyList<string>> GetGroupTransitiveUserIdsAsync(
        string groupId,
        CancellationToken cancellationToken = default)
    {
        GroupMemberReads.Add(groupId);
        return Task.FromResult(GroupMembers.TryGetValue(groupId, out var members) ? members : []);
    }

    public Task<bool> GetSecurityDefaultsEnabledAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(SecurityDefaultsEnabled);

    public Task<PerUserMfaState> GetPerUserMfaStateAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        PerUserMfaReads.Add(userId);
        return Task.FromResult(PerUserMfaStates.TryGetValue(userId, out var state) ? state : PerUserMfaState.Disabled);
    }

    public Task<IReadOnlyDictionary<string, SingleFactorSignIns>> GetSingleFactorSignInsAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        SignInReads.Add(since);
        return Task.FromResult(SignIns);
    }

    /// <summary>What <see cref="GetConditionalAccessUsageAsync"/> returns; no sign-ins unless set.</summary>
    public ConditionalAccessUsage? Usage { get; init; }

    /// <summary>The start passed to each <see cref="GetConditionalAccessUsageAsync"/> call.</summary>
    public List<DateTimeOffset> UsageReads { get; } = [];

    /// <summary>Reports the usage's sign-in count once, as if it were one page.</summary>
    public Task<ConditionalAccessUsage> GetConditionalAccessUsageAsync(
        DateTimeOffset since,
        IProgress<int>? signInsRead = null,
        CancellationToken cancellationToken = default)
    {
        UsageReads.Add(since);
        var usage = Usage ?? new ConditionalAccessUsageAggregator(since, since).ToUsage();
        signInsRead?.Report(usage.SignInsRead);
        return Task.FromResult(usage);
    }

    /// <summary>What <see cref="GetAuthorizationPolicyAsync"/> returns.</summary>
    public AuthorizationPolicySettings AuthorizationPolicy { get; init; } = new();

    /// <summary>When set, <see cref="GetAuthorizationPolicyAsync"/> throws it.</summary>
    public DirectoryReadException? AuthorizationPolicyFailure { get; init; }

    /// <summary>What <see cref="GetCrossTenantAccessDefaultsAsync"/> returns.</summary>
    public CrossTenantAccessSettings CrossTenantDefaults { get; init; } = new();

    /// <summary>When set, <see cref="GetCrossTenantAccessDefaultsAsync"/> throws it.</summary>
    public DirectoryReadException? CrossTenantDefaultsFailure { get; init; }

    public IReadOnlyList<CrossTenantAccessSettings> CrossTenantPartners { get; init; } = [];

    /// <summary>When set, <see cref="GetCrossTenantAccessPartnersAsync"/> throws it.</summary>
    public DirectoryReadException? CrossTenantPartnersFailure { get; init; }

    public Task<AuthorizationPolicySettings> GetAuthorizationPolicyAsync(CancellationToken cancellationToken = default) =>
        AuthorizationPolicyFailure is { } failure ? throw failure : Task.FromResult(AuthorizationPolicy);

    public Task<CrossTenantAccessSettings> GetCrossTenantAccessDefaultsAsync(
        CancellationToken cancellationToken = default) =>
        CrossTenantDefaultsFailure is { } failure ? throw failure : Task.FromResult(CrossTenantDefaults);

    public Task<IReadOnlyList<CrossTenantAccessSettings>> GetCrossTenantAccessPartnersAsync(
        CancellationToken cancellationToken = default) =>
        CrossTenantPartnersFailure is { } failure ? throw failure : Task.FromResult(CrossTenantPartners);
}
