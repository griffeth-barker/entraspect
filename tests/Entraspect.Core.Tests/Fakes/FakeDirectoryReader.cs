namespace Entraspect.Core.Tests;

internal sealed class FakeDirectoryReader : IDirectoryReader
{
    private readonly IReadOnlyList<AppRegistration> _apps;
    private readonly IReadOnlyList<DirectoryGroup> _groups;
    private readonly IReadOnlyList<EnterpriseApp> _enterpriseApps;
    private readonly IReadOnlyList<UserRegistration> _userRegistrations;

    public FakeDirectoryReader(
        IReadOnlyList<AppRegistration> apps,
        IReadOnlyList<DirectoryGroup>? groups = null,
        IReadOnlyList<EnterpriseApp>? enterpriseApps = null,
        IReadOnlyList<UserRegistration>? userRegistrations = null)
    {
        _apps = apps;
        _groups = groups ?? [];
        _enterpriseApps = enterpriseApps ?? [];
        _userRegistrations = userRegistrations ?? [];
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
}
