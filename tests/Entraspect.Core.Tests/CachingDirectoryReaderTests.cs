using System;
using System.Collections.Generic;
using System.Linq;
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

        public int GrantCalls { get; private set; }

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

        public Task<IReadOnlyList<AppPermissionGrant>> GetGraphApplicationPermissionGrantsAsync(
            CancellationToken cancellationToken = default)
        {
            GrantCalls++;
            return Task.FromResult<IReadOnlyList<AppPermissionGrant>>(Array.Empty<AppPermissionGrant>());
        }

        public int PolicyCalls { get; private set; }

        /// <summary>The user IDs passed to each lookup. IDs starting with "gone" are missing.</summary>
        public List<IReadOnlyCollection<string>> UserLookups { get; } = [];

        public Task<IReadOnlyList<ConditionalAccessPolicy>> GetConditionalAccessPoliciesAsync(
            CancellationToken cancellationToken = default)
        {
            PolicyCalls++;
            return Task.FromResult<IReadOnlyList<ConditionalAccessPolicy>>(Array.Empty<ConditionalAccessPolicy>());
        }

        public Task<IReadOnlyList<string>> GetMissingUserIdsAsync(
            IReadOnlyCollection<string> userIds,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            UserLookups.Add(userIds.ToList());
            var done = 0;
            foreach (var _ in userIds)
            {
                progress?.Report(++done);
            }

            return Task.FromResult<IReadOnlyList<string>>(
                userIds.Where(id => id.StartsWith("gone", StringComparison.Ordinal)).ToList());
        }
    }

    private sealed class FailingThenOkReader : IDirectoryReader
    {
        private bool _failed;
        private bool _groupsFailed;
        private bool _enterpriseAppsFailed;
        private bool _userRegistrationsFailed;
        private bool _grantsFailed;
        private bool _policiesFailed;
        private bool _usersFailed;

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

        public Task<IReadOnlyList<AppPermissionGrant>> GetGraphApplicationPermissionGrantsAsync(
            CancellationToken cancellationToken = default)
        {
            if (!_grantsFailed)
            {
                _grantsFailed = true;
                throw new DirectoryReadException("fail");
            }

            return Task.FromResult<IReadOnlyList<AppPermissionGrant>>(Array.Empty<AppPermissionGrant>());
        }

        public Task<IReadOnlyList<ConditionalAccessPolicy>> GetConditionalAccessPoliciesAsync(
            CancellationToken cancellationToken = default)
        {
            if (!_policiesFailed)
            {
                _policiesFailed = true;
                throw new DirectoryReadException("fail");
            }

            return Task.FromResult<IReadOnlyList<ConditionalAccessPolicy>>(Array.Empty<ConditionalAccessPolicy>());
        }

        public Task<IReadOnlyList<string>> GetMissingUserIdsAsync(
            IReadOnlyCollection<string> userIds,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (!_usersFailed)
            {
                _usersFailed = true;
                throw new DirectoryReadException("fail");
            }

            return Task.FromResult<IReadOnlyList<string>>(userIds.ToList());
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

    [Fact]
    public async Task Fetches_graph_application_permission_grants_only_once()
    {
        var inner = new CountingReader();
        var caching = new CachingDirectoryReader(inner);

        await caching.GetGraphApplicationPermissionGrantsAsync();
        await caching.GetGraphApplicationPermissionGrantsAsync();

        Assert.Equal(1, inner.GrantCalls);
    }

    [Fact]
    public async Task Does_not_cache_failed_graph_application_permission_grant_read()
    {
        var inner = new FailingThenOkReader();
        var caching = new CachingDirectoryReader(inner);

        await Assert.ThrowsAsync<DirectoryReadException>(() => caching.GetGraphApplicationPermissionGrantsAsync());
        var result = await caching.GetGraphApplicationPermissionGrantsAsync();

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Fetches_conditional_access_policies_only_once()
    {
        var inner = new CountingReader();
        var caching = new CachingDirectoryReader(inner);

        await caching.GetConditionalAccessPoliciesAsync();
        await caching.GetConditionalAccessPoliciesAsync();

        Assert.Equal(1, inner.PolicyCalls);
    }

    [Fact]
    public async Task Does_not_cache_failed_conditional_access_policy_read()
    {
        var inner = new FailingThenOkReader();
        var caching = new CachingDirectoryReader(inner);

        await Assert.ThrowsAsync<DirectoryReadException>(() => caching.GetConditionalAccessPoliciesAsync());
        var result = await caching.GetConditionalAccessPoliciesAsync();

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Looks_up_each_user_id_only_once()
    {
        var inner = new CountingReader();
        var caching = new CachingDirectoryReader(inner);

        var first = await caching.GetMissingUserIdsAsync(["a", "gone-1", "A"]);
        var second = await caching.GetMissingUserIdsAsync(["gone-1", "b", "gone-2"]);
        var third = await caching.GetMissingUserIdsAsync(["a", "b"]);

        Assert.Equal(new[] { "gone-1" }, first);
        Assert.Equal(new[] { "gone-1", "gone-2" }, second);
        Assert.Empty(third);
        Assert.Equal(new[] { new[] { "a", "gone-1" }, new[] { "b", "gone-2" } }, inner.UserLookups);
    }

    [Fact]
    public async Task User_lookup_progress_counts_ids_already_looked_up()
    {
        var inner = new CountingReader();
        var caching = new CachingDirectoryReader(inner);
        await caching.GetMissingUserIdsAsync(["a"]);
        var reports = new List<int>();

        await caching.GetMissingUserIdsAsync(["a", "b", "c"], new SyncProgress(reports.Add));

        Assert.Equal(new[] { 2, 3 }, reports);
    }

    [Fact]
    public async Task Does_not_cache_failed_user_lookup()
    {
        var inner = new FailingThenOkReader();
        var caching = new CachingDirectoryReader(inner);

        await Assert.ThrowsAsync<DirectoryReadException>(() => caching.GetMissingUserIdsAsync(["a"]));
        var result = await caching.GetMissingUserIdsAsync(["a"]);

        Assert.Equal(new[] { "a" }, result);
    }

    // Progress<T> reports on the thread pool; this one reports at once, so the test can read the values.
    private sealed class SyncProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }
}
