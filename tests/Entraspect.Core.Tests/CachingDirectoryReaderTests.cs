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

        public int LicenseErrorCalls { get; private set; }

        public Task<IReadOnlyList<string>> GetGroupIdsWithLicenseErrorsAsync(
            CancellationToken cancellationToken = default)
        {
            LicenseErrorCalls++;
            return Task.FromResult<IReadOnlyList<string>>(["g1"]);
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

        public int UserCalls { get; private set; }

        public int RoleAssignmentCalls { get; private set; }

        public int SecurityDefaultsCalls { get; private set; }

        public List<string> GroupMemberReads { get; } = [];

        public List<string> PerUserMfaReads { get; } = [];

        public List<DateTimeOffset> SignInReads { get; } = [];

        public Task<IReadOnlyList<DirectoryUser>> GetUsersAsync(CancellationToken cancellationToken = default)
        {
            UserCalls++;
            return Task.FromResult<IReadOnlyList<DirectoryUser>>(Array.Empty<DirectoryUser>());
        }

        public Task<IReadOnlyList<DirectoryRoleAssignment>> GetDirectoryRoleAssignmentsAsync(
            CancellationToken cancellationToken = default)
        {
            RoleAssignmentCalls++;
            return Task.FromResult<IReadOnlyList<DirectoryRoleAssignment>>(Array.Empty<DirectoryRoleAssignment>());
        }

        public Task<IReadOnlyList<string>> GetGroupTransitiveUserIdsAsync(
            string groupId,
            CancellationToken cancellationToken = default)
        {
            GroupMemberReads.Add(groupId);
            return Task.FromResult<IReadOnlyList<string>>([$"member-of-{groupId}"]);
        }

        public Task<bool> GetSecurityDefaultsEnabledAsync(CancellationToken cancellationToken = default)
        {
            SecurityDefaultsCalls++;
            return Task.FromResult(true);
        }

        public Task<PerUserMfaState> GetPerUserMfaStateAsync(
            string userId,
            CancellationToken cancellationToken = default)
        {
            PerUserMfaReads.Add(userId);
            return Task.FromResult(PerUserMfaState.Enforced);
        }

        public Task<IReadOnlyDictionary<string, SingleFactorSignIns>> GetSingleFactorSignInsAsync(
            DateTimeOffset since,
            CancellationToken cancellationToken = default)
        {
            SignInReads.Add(since);
            return Task.FromResult<IReadOnlyDictionary<string, SingleFactorSignIns>>(
                new Dictionary<string, SingleFactorSignIns> { ["u1"] = new(1, since) });
        }

        public List<DateTimeOffset> UsageReads { get; } = [];

        public Task<ConditionalAccessUsage> GetConditionalAccessUsageAsync(
            DateTimeOffset since,
            IProgress<int>? signInsRead = null,
            CancellationToken cancellationToken = default)
        {
            UsageReads.Add(since);
            var aggregator = new ConditionalAccessUsageAggregator(since, since);
            aggregator.Add([new AppliedPolicyResult("p1", ConditionalAccessResult.Success)]);
            aggregator.Add([]);
            signInsRead?.Report(1);
            signInsRead?.Report(2);
            return Task.FromResult(aggregator.ToUsage());
        }
    }

    private sealed class FailingThenOkReader : IDirectoryReader
    {
        private bool _failed;
        private bool _groupsFailed;
        private bool _licenseErrorsFailed;
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

        public Task<IReadOnlyList<string>> GetGroupIdsWithLicenseErrorsAsync(
            CancellationToken cancellationToken = default)
        {
            if (!_licenseErrorsFailed)
            {
                _licenseErrorsFailed = true;
                throw new DirectoryReadException("fail");
            }

            return Task.FromResult<IReadOnlyList<string>>(["g1"]);
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

        private int _newReadFailures;

        // Each new read fails on its first call only.
        private void FailOnce(string name)
        {
            if ((_newReadFailures & Flag(name)) == 0)
            {
                _newReadFailures |= Flag(name);
                throw new DirectoryReadException("fail");
            }
        }

        private static int Flag(string name) => name switch
        {
            "users" => 1,
            "roles" => 2,
            "group" => 4,
            "defaults" => 8,
            "mfa" => 16,
            _ => 32,
        };

        public Task<IReadOnlyList<DirectoryUser>> GetUsersAsync(CancellationToken cancellationToken = default)
        {
            FailOnce("users");
            return Task.FromResult<IReadOnlyList<DirectoryUser>>(Array.Empty<DirectoryUser>());
        }

        public Task<IReadOnlyList<DirectoryRoleAssignment>> GetDirectoryRoleAssignmentsAsync(
            CancellationToken cancellationToken = default)
        {
            FailOnce("roles");
            return Task.FromResult<IReadOnlyList<DirectoryRoleAssignment>>(Array.Empty<DirectoryRoleAssignment>());
        }

        public Task<IReadOnlyList<string>> GetGroupTransitiveUserIdsAsync(
            string groupId,
            CancellationToken cancellationToken = default)
        {
            FailOnce("group");
            return Task.FromResult<IReadOnlyList<string>>(["u1"]);
        }

        public Task<bool> GetSecurityDefaultsEnabledAsync(CancellationToken cancellationToken = default)
        {
            FailOnce("defaults");
            return Task.FromResult(true);
        }

        public Task<PerUserMfaState> GetPerUserMfaStateAsync(
            string userId,
            CancellationToken cancellationToken = default)
        {
            FailOnce("mfa");
            return Task.FromResult(PerUserMfaState.Enabled);
        }

        public Task<IReadOnlyDictionary<string, SingleFactorSignIns>> GetSingleFactorSignInsAsync(
            DateTimeOffset since,
            CancellationToken cancellationToken = default)
        {
            FailOnce("signins");
            return Task.FromResult<IReadOnlyDictionary<string, SingleFactorSignIns>>(
                new Dictionary<string, SingleFactorSignIns>());
        }

        public Task<ConditionalAccessUsage> GetConditionalAccessUsageAsync(
            DateTimeOffset since,
            IProgress<int>? signInsRead = null,
            CancellationToken cancellationToken = default)
        {
            FailOnce("usage");
            return Task.FromResult(new ConditionalAccessUsageAggregator(since, since).ToUsage());
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
    public async Task Fetches_groups_with_licence_errors_only_once()
    {
        var inner = new CountingReader();
        var caching = new CachingDirectoryReader(inner);

        await caching.GetGroupIdsWithLicenseErrorsAsync();
        var result = await caching.GetGroupIdsWithLicenseErrorsAsync();

        Assert.Equal(new[] { "g1" }, result);
        Assert.Equal(1, inner.LicenseErrorCalls);
        Assert.Equal(0, inner.GroupCalls);
    }

    [Fact]
    public async Task Does_not_cache_failed_licence_error_read()
    {
        var inner = new FailingThenOkReader();
        var caching = new CachingDirectoryReader(inner);

        await Assert.ThrowsAsync<DirectoryReadException>(() => caching.GetGroupIdsWithLicenseErrorsAsync());

        Assert.Equal(new[] { "g1" }, await caching.GetGroupIdsWithLicenseErrorsAsync());
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

        await caching.GetMissingUserIdsAsync(["a", "b", "c"], new SyncProgress<int>(reports.Add));

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

    [Fact]
    public async Task Fetches_users_role_assignments_and_security_defaults_only_once()
    {
        var inner = new CountingReader();
        var caching = new CachingDirectoryReader(inner);

        await caching.GetUsersAsync();
        await caching.GetUsersAsync();
        await caching.GetDirectoryRoleAssignmentsAsync();
        await caching.GetDirectoryRoleAssignmentsAsync();
        Assert.True(await caching.GetSecurityDefaultsEnabledAsync());
        Assert.True(await caching.GetSecurityDefaultsEnabledAsync());

        Assert.Equal((1, 1, 1), (inner.UserCalls, inner.RoleAssignmentCalls, inner.SecurityDefaultsCalls));
    }

    [Fact]
    public async Task Reads_each_group_and_each_user_mfa_state_only_once()
    {
        var inner = new CountingReader();
        var caching = new CachingDirectoryReader(inner);

        Assert.Equal(new[] { "member-of-g1" }, await caching.GetGroupTransitiveUserIdsAsync("g1"));
        await caching.GetGroupTransitiveUserIdsAsync("G1");
        await caching.GetGroupTransitiveUserIdsAsync("g2");
        Assert.Equal(PerUserMfaState.Enforced, await caching.GetPerUserMfaStateAsync("u1"));
        await caching.GetPerUserMfaStateAsync("U1");
        await caching.GetPerUserMfaStateAsync("u2");

        Assert.Equal(new[] { "g1", "g2" }, inner.GroupMemberReads);
        Assert.Equal(new[] { "u1", "u2" }, inner.PerUserMfaReads);
    }

    [Fact]
    public async Task Reads_sign_ins_once_for_the_same_start()
    {
        var inner = new CountingReader();
        var caching = new CachingDirectoryReader(inner);
        var since = new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero);

        await caching.GetSingleFactorSignInsAsync(since);
        var again = await caching.GetSingleFactorSignInsAsync(since);
        await caching.GetSingleFactorSignInsAsync(since.AddDays(-1));

        Assert.Equal(1, again["u1"].Count);
        Assert.Equal(new[] { since, since.AddDays(-1) }, inner.SignInReads);
    }

    [Fact]
    public async Task Does_not_cache_failed_reads_of_the_mfa_requirement_data()
    {
        var inner = new FailingThenOkReader();
        var caching = new CachingDirectoryReader(inner);

        await Assert.ThrowsAsync<DirectoryReadException>(() => caching.GetUsersAsync());
        Assert.NotNull(await caching.GetUsersAsync());
        await Assert.ThrowsAsync<DirectoryReadException>(() => caching.GetDirectoryRoleAssignmentsAsync());
        Assert.NotNull(await caching.GetDirectoryRoleAssignmentsAsync());
        await Assert.ThrowsAsync<DirectoryReadException>(() => caching.GetGroupTransitiveUserIdsAsync("g1"));
        Assert.Equal(new[] { "u1" }, await caching.GetGroupTransitiveUserIdsAsync("g1"));
        await Assert.ThrowsAsync<DirectoryReadException>(() => caching.GetSecurityDefaultsEnabledAsync());
        Assert.True(await caching.GetSecurityDefaultsEnabledAsync());
        await Assert.ThrowsAsync<DirectoryReadException>(() => caching.GetPerUserMfaStateAsync("u1"));
        Assert.Equal(PerUserMfaState.Enabled, await caching.GetPerUserMfaStateAsync("u1"));
        await Assert.ThrowsAsync<DirectoryReadException>(() => caching.GetSingleFactorSignInsAsync(default));
        Assert.Empty(await caching.GetSingleFactorSignInsAsync(default));
    }

    [Fact]
    public async Task Reads_conditional_access_usage_once_for_the_same_start()
    {
        var inner = new CountingReader();
        var caching = new CachingDirectoryReader(inner);
        var since = new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero);
        var reported = new List<int>();
        var progress = new SyncProgress<int>(reported.Add);

        await caching.GetConditionalAccessUsageAsync(since, progress);
        var again = await caching.GetConditionalAccessUsageAsync(since, progress);
        await caching.GetConditionalAccessUsageAsync(since.AddDays(-1));

        Assert.Equal(2, again.SignInsRead);
        Assert.Equal(1, again.Count("p1", ConditionalAccessResult.Success));
        Assert.Equal(new[] { since, since.AddDays(-1) }, inner.UsageReads);
        // The cached read reports its total once.
        Assert.Equal(new[] { 1, 2, 2 }, reported);
    }

    [Fact]
    public async Task Does_not_cache_a_failed_conditional_access_usage_read()
    {
        var inner = new FailingThenOkReader();
        var caching = new CachingDirectoryReader(inner);

        await Assert.ThrowsAsync<DirectoryReadException>(() => caching.GetConditionalAccessUsageAsync(default));
        Assert.Equal(0, (await caching.GetConditionalAccessUsageAsync(default)).SignInsRead);
    }
}
