using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Entraspect.Core.Tests;

public sealed class CachingDirectoryReaderTests
{
    private sealed class CountingReader : IDirectoryReader
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<AppRegistration>> GetAppRegistrationsAsync(
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<AppRegistration>>(Array.Empty<AppRegistration>());
        }
    }

    private sealed class FailingThenOkReader : IDirectoryReader
    {
        private bool _failed;

        public Task<IReadOnlyList<AppRegistration>> GetAppRegistrationsAsync(
            CancellationToken cancellationToken = default)
        {
            if (!_failed)
            {
                _failed = true;
                throw new DirectoryReadException("fail");
            }

            return Task.FromResult<IReadOnlyList<AppRegistration>>(Array.Empty<AppRegistration>());
        }
    }

    [Fact]
    public async Task Fetches_each_collection_only_once()
    {
        var inner = new CountingReader();
        var caching = new CachingDirectoryReader(inner);

        await caching.GetAppRegistrationsAsync();
        await caching.GetAppRegistrationsAsync();
        await caching.GetAppRegistrationsAsync();

        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task Does_not_cache_failed_read()
    {
        var inner = new FailingThenOkReader();
        var caching = new CachingDirectoryReader(inner);

        await Assert.ThrowsAsync<DirectoryReadException>(() => caching.GetAppRegistrationsAsync());
        var result = await caching.GetAppRegistrationsAsync();

        Assert.NotNull(result);
    }
}
