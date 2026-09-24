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
    private bool _appRegistrationsLoaded;

    public CachingDirectoryReader(IDirectoryReader inner) => _inner = inner;

    public async Task<IReadOnlyList<AppRegistration>> GetAppRegistrationsAsync(
        CancellationToken cancellationToken = default)
    {
        if (_appRegistrationsLoaded)
        {
            return _appRegistrations!;
        }

        var apps = await _inner.GetAppRegistrationsAsync(cancellationToken).ConfigureAwait(false);
        _appRegistrations = apps;
        _appRegistrationsLoaded = true;
        return apps;
    }
}
