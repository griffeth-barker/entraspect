namespace Entraspect.Core.Tests;

internal sealed class FakeDirectoryReader : IDirectoryReader
{
    private readonly IReadOnlyList<AppRegistration> _apps;
    private readonly IReadOnlyList<DirectoryGroup> _groups;

    public FakeDirectoryReader(IReadOnlyList<AppRegistration> apps, IReadOnlyList<DirectoryGroup>? groups = null)
    {
        _apps = apps;
        _groups = groups ?? [];
    }

    public FakeDirectoryReader(IReadOnlyList<DirectoryGroup> groups)
        : this([], groups)
    {
    }

    public Task<IReadOnlyList<AppRegistration>> GetAppRegistrationsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_apps);

    public Task<IReadOnlyList<DirectoryGroup>> GetGroupsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_groups);
}
