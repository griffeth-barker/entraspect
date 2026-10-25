using System.Collections.Generic;
using Entraspect.Core.Checks;
using static Entraspect.Core.Tests.Checks.AdminTestData;

namespace Entraspect.Core.Tests.Checks;

public sealed class RolesSyncedAdminsCheckTests
{
    private const string Message =
        "Admin account is synced from on-premises Active Directory. Anyone who takes over that account or the sync " +
        "server gets these roles. Use a cloud-only account for admin work.";

    private static Task<IReadOnlyList<Finding>> RunAsync(
        DirectoryUser user,
        DirectoryRoleAssignment[] assignments,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? groups = null) =>
        new RolesSyncedAdminsCheck().RunAsync(Reader([user], assignments, groups), new CheckOptions());

    [Theory]
    [InlineData(PrivilegedRoleAdministrator, Severity.Critical, "Privileged Role Administrator")]
    [InlineData(SecurityAdministrator, Severity.Warning, "Security Administrator")]
    public async Task Synced_admins_are_reported_by_their_highest_tier(string role, Severity expected, string roles)
    {
        var finding = Assert.Single(await RunAsync(User(1, synced: true), [Assign(role, UserId(1))]));

        Assert.Equal(expected, finding.Severity);
        Assert.Equal(Message, finding.Message);
        Assert.Equal(("roles-synced-admins", "User", UserId(1), "User 1", "user1@contoso.com"),
            (finding.CheckId, finding.ObjectType, finding.ObjectId, finding.ObjectDisplayName, finding.UserPrincipalName));
        Assert.Equal(roles, finding.SubObjectDisplayName);
        Assert.Null(finding.Value);
    }

    [Fact]
    public async Task Roles_are_listed_highest_tier_first_and_only_privileged_ones()
    {
        var finding = Assert.Single(await RunAsync(
            User(1, synced: true),
            [
                Assign(DirectoryReaders, UserId(1)),
                Assign(SecurityAdministrator, UserId(1)),
                Assign(PrivilegedRoles.GlobalAdministrator, UserId(1)),
                Assign(GlobalReader, UserId(1)),
            ]));

        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal("Global Administrator, Global Reader, Security Administrator", finding.SubObjectDisplayName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task Cloud_admins_are_not_reported(bool? synced)
    {
        Assert.Empty(await RunAsync(User(1, synced: synced), [Assign(PrivilegedRoles.GlobalAdministrator, UserId(1))]));
    }

    [Fact]
    public async Task Synced_users_with_only_other_roles_are_not_reported()
    {
        Assert.Empty(await RunAsync(User(1, synced: true), [Assign(DirectoryReaders, UserId(1))]));
    }

    [Fact]
    public async Task A_synced_user_holding_a_role_through_a_group_is_reported()
    {
        var finding = Assert.Single(await RunAsync(
            User(1, synced: true),
            [Assign(PrivilegedRoles.GlobalAdministrator, RoleGroupId)],
            new Dictionary<string, IReadOnlyList<string>> { [RoleGroupId] = [UserId(1)] }));

        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal("Global Administrator", finding.SubObjectDisplayName);
    }

    [Fact]
    public async Task Disabled_synced_admins_are_reported()
    {
        Assert.Single(await RunAsync(
            User(1, enabled: false, synced: true),
            [Assign(PrivilegedRoles.GlobalAdministrator, UserId(1))]));
    }

    [Fact]
    public async Task Fails_on_its_own_when_a_read_fails()
    {
        var reader = new FakeDirectoryReader(apps: []) { UsersFailure = new DirectoryReadException("denied", 403) };

        await Assert.ThrowsAsync<DirectoryReadException>(
            () => new RolesSyncedAdminsCheck().RunAsync(reader, new CheckOptions()));
    }
}
