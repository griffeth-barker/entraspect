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

        public int GroupCalls { get; private set; }

        public int EnterpriseAppCalls { get; private set; }

        public int UserRegistrationCalls { get; private set; }

        public Task<IReadOnlyList<AppRegistration>> GetAppRegistrationsAsync(
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<AppRegistration>>(Array.Empty<AppRegistration>());
        }

        public Task<IReadOnlyList<DirectoryGroup>> GetGroupsAsync(CancellationToken cancellationToken = default)
        {
            GroupCalls++;
            return Task.FromResult<IReadOnlyList<DirectoryGroup>>(Array.Empty<DirectoryGroup>());
        }

        public Task<IReadOnlyList<EnterpriseApp>> GetEnterpriseAppsAsync(CancellationToken cancellationToken = default)
        {
            EnterpriseAppCalls++;
            return Task.FromResult<IReadOnlyList<EnterpriseApp>>(Array.Empty<EnterpriseApp>());
        }

        public Task<IReadOnlyList<UserRegistration>> GetUserRegistrationsAsync(
            CancellationToken cancellationToken = default)
        {
            UserRegistrationCalls++;
            return Task.FromResult<IReadOnlyList<UserRegistration>>(Array.Empty<UserRegistration>());
        }
    }

    private sealed class FailingThenOkReader : IDirectoryReader
    {
        private bool _failed;
        private bool _groupsFailed;
        private bool _enterpriseAppsFailed;
        private bool _userRegistrationsFailed;

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

        public Task<IReadOnlyList<DirectoryGroup>> GetGroupsAsync(CancellationToken cancellationToken = default)
        {
            if (!_groupsFailed)
            {
                _groupsFailed = true;
                throw new DirectoryReadException("fail");
            }

            return Task.FromResult<IReadOnlyList<DirectoryGroup>>(Array.Empty<DirectoryGroup>());
        }

        public Task<IReadOnlyList<EnterpriseApp>> GetEnterpriseAppsAsync(CancellationToken cancellationToken = default)
        {
            if (!_enterpriseAppsFailed)
            {
                _enterpriseAppsFailed = true;
                throw new DirectoryReadException("fail");
            }

            return Task.FromResult<IReadOnlyList<EnterpriseApp>>(Array.Empty<EnterpriseApp>());
        }

        public Task<IReadOnlyList<UserRegistration>> GetUserRegistrationsAsync(
            CancellationToken cancellationToken = default)
        {
            if (!_userRegistrationsFailed)
            {
                _userRegistrationsFailed = true;
                throw new DirectoryReadException("fail");
            }

            return Task.FromResult<IReadOnlyList<UserRegistration>>(Array.Empty<UserRegistration>());
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
    public async Task Fetches_groups_only_once()
    {
        var inner = new CountingReader();
        var caching = new CachingDirectoryReader(inner);

        await caching.GetGroupsAsync();
        await caching.GetGroupsAsync();

        Assert.Equal(1, inner.GroupCalls);
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

    [Fact]
    public async Task Does_not_cache_failed_group_read()
    {
        var inner = new FailingThenOkReader();
        var caching = new CachingDirectoryReader(inner);

        await Assert.ThrowsAsync<DirectoryReadException>(() => caching.GetGroupsAsync());
        var result = await caching.GetGroupsAsync();

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Fetches_enterprise_apps_only_once()
    {
        var inner = new CountingReader();
        var caching = new CachingDirectoryReader(inner);

        await caching.GetEnterpriseAppsAsync();
        await caching.GetEnterpriseAppsAsync();

        Assert.Equal(1, inner.EnterpriseAppCalls);
    }

    [Fact]
    public async Task Does_not_cache_failed_enterprise_app_read()
    {
        var inner = new FailingThenOkReader();
        var caching = new CachingDirectoryReader(inner);

        await Assert.ThrowsAsync<DirectoryReadException>(() => caching.GetEnterpriseAppsAsync());
        var result = await caching.GetEnterpriseAppsAsync();

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Fetches_user_registrations_only_once()
    {
        var inner = new CountingReader();
        var caching = new CachingDirectoryReader(inner);

        await caching.GetUserRegistrationsAsync();
        await caching.GetUserRegistrationsAsync();

        Assert.Equal(1, inner.UserRegistrationCalls);
    }

    [Fact]
    public async Task Does_not_cache_failed_user_registration_read()
    {
        var inner = new FailingThenOkReader();
        var caching = new CachingDirectoryReader(inner);

        await Assert.ThrowsAsync<DirectoryReadException>(() => caching.GetUserRegistrationsAsync());
        var result = await caching.GetUserRegistrationsAsync();

        Assert.NotNull(result);
    }
}
