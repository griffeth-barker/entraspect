using System.Collections.Generic;
using System.Linq;
using Entraspect.Core.Checks;

namespace Entraspect.Core.Tests.Checks;

public sealed class UsersDisabledAccessCheckTests
{
    private const string UserId = "aaaaaaaa-0000-0000-0000-000000000001";
    private const string RoleGroupId = "99999999-0000-0000-0000-000000000001";
    private const string GlobalReader = "f2ef992c-3afb-46b9-b7cf-a126ee74c451";
    private const string DirectoryReaders = "88d8e3e3-8f55-4a1e-953a-9b9898b8876b";

    private static readonly DirectoryRoleDefinition[] Definitions =
    [
        new() { Id = PrivilegedRoles.GlobalAdministrator, TemplateId = PrivilegedRoles.GlobalAdministrator, DisplayName = "Global Administrator" },
        new() { Id = GlobalReader, TemplateId = GlobalReader, DisplayName = "Global Reader" },
        new() { Id = DirectoryReaders, TemplateId = DirectoryReaders, DisplayName = "Directory Readers" },
    ];

    private static DirectoryUser User(
        bool enabled = false,
        int licences = 0,
        bool? synced = null,
        UserType type = UserType.Member) =>
        new()
        {
            Id = UserId,
            UserPrincipalName = "user@contoso.com",
            DisplayName = "User 1",
            UserType = type,
            AccountEnabled = enabled,
            AssignedLicenceCount = licences,
            OnPremisesSyncEnabled = synced,
        };

    private static DirectoryRoleAssignment Assign(string role, string principal = UserId) =>
        new() { RoleDefinitionId = role, PrincipalId = principal };

    private static Task<IReadOnlyList<Finding>> RunAsync(
        DirectoryUser user,
        DirectoryRoleAssignment[]? assignments = null,
        Dictionary<string, IReadOnlyList<string>>? groups = null) =>
        new UsersDisabledAccessCheck().RunAsync(
            new FakeDirectoryReader(apps: [])
            {
                Users = [user],
                RoleAssignments = assignments ?? [],
                RoleDefinitions = Definitions,
                GroupMembers = groups ?? new Dictionary<string, IReadOnlyList<string>>(),
            },
            new CheckOptions());

    [Theory]
    [InlineData(PrivilegedRoles.GlobalAdministrator, "Global Administrator", Severity.Critical)]
    [InlineData(GlobalReader, "Global Reader", Severity.Critical)]
    [InlineData(DirectoryReaders, "Directory Readers", Severity.Warning)]
    public async Task Role_severity_follows_its_tier(string role, string name, Severity expected)
    {
        var finding = Assert.Single(await RunAsync(User(), [Assign(role)]));

        Assert.Equal(expected, finding.Severity);
        Assert.Equal(
            $"Disabled account still holds {name}. Re-enabling it restores admin access; remove the role.",
            finding.Message);
        Assert.Equal(name, finding.SubObjectDisplayName);
        Assert.Equal(("users-disabled-access", "User", UserId), (finding.CheckId, finding.ObjectType, finding.ObjectId));
        Assert.Equal("user@contoso.com", finding.UserPrincipalName);
        Assert.Equal("Cloud", finding.Value);
        Assert.Null(finding.DueDate);
    }

    [Fact]
    public async Task One_finding_per_role()
    {
        var findings = await RunAsync(
            User(),
            [Assign(DirectoryReaders), Assign(PrivilegedRoles.GlobalAdministrator), Assign(DirectoryReaders, RoleGroupId)],
            new() { [RoleGroupId] = [UserId] });

        Assert.Equal(["Global Administrator", "Directory Readers"], findings.Select(f => f.SubObjectDisplayName));
    }

    [Fact]
    public async Task Role_through_a_group_counts()
    {
        var finding = Assert.Single(await RunAsync(
            User(),
            [Assign(PrivilegedRoles.GlobalAdministrator, RoleGroupId)],
            new() { [RoleGroupId] = [UserId] }));

        Assert.Equal(Severity.Critical, finding.Severity);
    }

    [Theory]
    [InlineData(1, "1 licence")]
    [InlineData(3, "3 licences")]
    public async Task Licences_are_info(int count, string text)
    {
        var finding = Assert.Single(await RunAsync(User(licences: count)));

        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Equal($"Disabled account still has {text} assigned. Remove them if the user has left.", finding.Message);
        Assert.Equal("Licences", finding.SubObjectDisplayName);
    }

    [Fact]
    public async Task Roles_and_licences_each_get_a_finding()
    {
        var findings = await RunAsync(User(licences: 2), [Assign(GlobalReader)]);

        Assert.Equal(["Global Reader", "Licences"], findings.Select(f => f.SubObjectDisplayName));
    }

    [Fact]
    public async Task Synced_users_say_they_are_managed_on_premises()
    {
        var findings = await RunAsync(User(licences: 1, synced: true), [Assign(GlobalReader)]);

        Assert.All(findings, f => Assert.EndsWith(" This account is managed in on-premises Active Directory.", f.Message));
        Assert.All(findings, f => Assert.Equal("Synced", f.Value));
    }

    [Fact]
    public async Task Disabled_user_without_access_is_not_reported()
    {
        Assert.Empty(await RunAsync(User()));
    }

    [Fact]
    public async Task Enabled_users_are_ignored()
    {
        Assert.Empty(await RunAsync(User(enabled: true, licences: 1), [Assign(PrivilegedRoles.GlobalAdministrator)]));
    }

    [Fact]
    public async Task Guests_are_ignored()
    {
        Assert.Empty(await RunAsync(User(licences: 1, type: UserType.Guest), [Assign(PrivilegedRoles.GlobalAdministrator)]));
    }

    [Fact]
    public async Task Fails_when_role_assignments_cannot_be_read()
    {
        var reader = new FakeDirectoryReader(apps: [])
        {
            Users = [User(licences: 1)],
            RoleAssignmentsFailure = new DirectoryReadException("denied", 403),
        };

        await Assert.ThrowsAsync<DirectoryReadException>(() => new UsersDisabledAccessCheck().RunAsync(reader, new CheckOptions()));
    }
}
