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
}
