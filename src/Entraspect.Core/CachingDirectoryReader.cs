using System.Collections.Generic;
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
}
