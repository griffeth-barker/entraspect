using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Entraspect.Core.Tests;

internal sealed class FakeDirectoryReader : IDirectoryReader
{
    private readonly IReadOnlyList<AppRegistration> _apps;

    public FakeDirectoryReader(IReadOnlyList<AppRegistration> apps) => _apps = apps;

    public Task<IReadOnlyList<AppRegistration>> GetAppRegistrationsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_apps);
}
