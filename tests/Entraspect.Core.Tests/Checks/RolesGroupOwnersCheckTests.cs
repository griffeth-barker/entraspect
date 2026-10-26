using System.Collections.Generic;
using System.Linq;
using Entraspect.Core.Checks;
using static Entraspect.Core.Tests.Checks.AdminTestData;

namespace Entraspect.Core.Tests.Checks;

public sealed class RolesGroupOwnersCheckTests
{
    private const string Message =
        "Owner can add anyone to this group, including themselves, and so give them its roles. Remove the owner, or " +
        "protect the account like an admin with these roles.";

    private const string OtherGroupId = "99999999-0000-0000-0000-000000000002";
    private const string ServicePrincipalId = "bbbbbbbb-0000-0000-0000-000000000001";
    private const string CustomRoleId = "cccccccc-0000-0000-0000-000000000001";

    private static DirectoryGroup Group(string id, bool roleAssignable = true) =>
        new()
        {
            Id = id,
            DisplayName = $"Group {id[^1]}",
            Kind = GroupKind.Security,
            IsDynamic = false,
            IsSyncedFromOnPremises = false,
            IsRoleAssignable = roleAssignable,
            HasMembers = true,
            OwnerCount = 1,
        };

    private static GroupOwner UserOwner(int n, string? displayName = null, string? upn = null) =>
        new() { Id = UserId(n), Kind = GroupOwnerKind.User, DisplayName = displayName, UserPrincipalName = upn };

    private static FakeDirectoryReader Reader(
        DirectoryRoleAssignment[] assignments,
        Dictionary<string, IReadOnlyList<GroupOwner>> owners,
        IReadOnlyList<DirectoryUser>? users = null,
        IReadOnlyList<DirectoryGroup>? groups = null,
        IReadOnlyList<DirectoryRoleDefinition>? definitions = null) =>
        new(apps: [], groups: groups ?? [Group(RoleGroupId)])
        {
            Users = users ?? [User(1), User(2), User(3)],
            RoleAssignments = assignments,
            RoleDefinitions = definitions ?? Definitions,
            GroupOwners = owners,
        };

    private static Task<IReadOnlyList<Finding>> RunAsync(FakeDirectoryReader reader) =>
        new RolesGroupOwnersCheck().RunAsync(reader, new CheckOptions());

    private static Dictionary<string, IReadOnlyList<GroupOwner>> Owners(params GroupOwner[] owners) =>
        new() { [RoleGroupId] = owners };

    [Theory]
    [InlineData(PrivilegedRoleAdministrator, Severity.Critical, "Privileged Role Administrator")]
    [InlineData(SecurityAdministrator, Severity.Warning, "Security Administrator")]
    public async Task Owner_of_a_group_holding_a_privileged_role_is_reported_by_the_highest_tier(
        string role,
        Severity expected,
        string roles)
    {
        var finding = Assert.Single(await RunAsync(Reader([Assign(role, RoleGroupId)], Owners(UserOwner(1)))));

        Assert.Equal(expected, finding.Severity);
        Assert.Equal(Message, finding.Message);
        Assert.Equal(("roles-group-owners", "Group", RoleGroupId, "Group 1"),
            (finding.CheckId, finding.ObjectType, finding.ObjectId, finding.ObjectDisplayName));
        Assert.Equal(("User", "User 1", "user1@contoso.com"),
            (finding.SubObjectType, finding.SubObjectDisplayName, finding.UserPrincipalName));
        Assert.Equal(roles, finding.Value);
        Assert.Null(finding.DueDate);
    }

    [Fact]
    public async Task Roles_are_listed_tier_0_first_then_by_name_and_only_privileged_ones()
    {
        var finding = Assert.Single(await RunAsync(Reader(
            [
                Assign(DirectoryReaders, RoleGroupId),
                Assign(SecurityAdministrator, RoleGroupId),
                Assign(GlobalReader, RoleGroupId),
                Assign(PrivilegedRoles.GlobalAdministrator, RoleGroupId),
            ],
            Owners(UserOwner(1)))));

        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal("Global Administrator, Global Reader, Security Administrator", finding.Value);
    }

    [Fact]
    public async Task Group_holding_only_other_roles_is_not_reported_and_its_owners_are_not_read()
    {
        var definitions = Definitions.Append(new DirectoryRoleDefinition { Id = CustomRoleId, DisplayName = "Helpdesk Lite" })
            .ToList();
        var reader = Reader(
            [Assign(DirectoryReaders, RoleGroupId), Assign(CustomRoleId, RoleGroupId)],
            Owners(UserOwner(1)),
            definitions: definitions);

        Assert.Empty(await RunAsync(reader));
        Assert.Empty(reader.GroupOwnerReads);
    }

    [Fact]
    public async Task Role_assignable_group_without_a_role_is_not_reported_and_its_owners_are_not_read()
    {
        var reader = Reader([Assign(PrivilegedRoles.GlobalAdministrator, UserId(2))], Owners(UserOwner(1)));

        Assert.Empty(await RunAsync(reader));
        Assert.Empty(reader.GroupOwnerReads);
    }

    [Fact]
    public async Task Group_that_is_not_role_assignable_is_not_reported()
    {
        var reader = Reader(
            [Assign(PrivilegedRoles.GlobalAdministrator, RoleGroupId)],
            Owners(UserOwner(1)),
            groups: [Group(RoleGroupId, roleAssignable: false)]);

        Assert.Empty(await RunAsync(reader));
        Assert.Empty(reader.GroupOwnerReads);
    }

    [Fact]
    public async Task Group_without_owners_is_not_reported()
    {
        var reader = Reader([Assign(PrivilegedRoles.GlobalAdministrator, RoleGroupId)], []);

        Assert.Empty(await RunAsync(reader));
        Assert.Equal([RoleGroupId], reader.GroupOwnerReads);
    }

    [Fact]
    public async Task Each_owner_of_each_group_is_reported_once()
    {
        var reader = Reader(
            [Assign(PrivilegedRoles.GlobalAdministrator, RoleGroupId), Assign(SecurityAdministrator, OtherGroupId)],
            new Dictionary<string, IReadOnlyList<GroupOwner>>
            {
                [RoleGroupId] = [UserOwner(1), UserOwner(2), UserOwner(1)],
                [OtherGroupId] = [UserOwner(1)],
            },
            groups: [Group(RoleGroupId), Group(OtherGroupId), Group(RoleGroupId)]);

        var findings = await RunAsync(reader);

        Assert.Equal(
            [(RoleGroupId, "User 1", Severity.Critical), (RoleGroupId, "User 2", Severity.Critical),
                (OtherGroupId, "User 1", Severity.Warning)],
            findings.Select(f => (f.ObjectId, f.SubObjectDisplayName, f.Severity)));
        Assert.Equal([RoleGroupId, OtherGroupId], reader.GroupOwnerReads);
    }

    [Fact]
    public async Task Service_principal_owner_is_reported_without_a_upn()
    {
        var finding = Assert.Single(await RunAsync(Reader(
            [Assign(PrivilegedRoles.GlobalAdministrator, RoleGroupId)],
            Owners(new GroupOwner { Id = ServicePrincipalId, Kind = GroupOwnerKind.ServicePrincipal, DisplayName = "Deploy app" }))));

        Assert.Equal(("Service principal", "Deploy app"), (finding.SubObjectType, finding.SubObjectDisplayName));
        Assert.Null(finding.UserPrincipalName);
    }

    [Fact]
    public async Task Owner_without_a_name_falls_back_to_the_id()
    {
        var finding = Assert.Single(await RunAsync(Reader(
            [Assign(PrivilegedRoles.GlobalAdministrator, RoleGroupId)],
            Owners(new GroupOwner { Id = ServicePrincipalId, Kind = GroupOwnerKind.Other }))));

        Assert.Equal(("Other", ServicePrincipalId), (finding.SubObjectType, finding.SubObjectDisplayName));
    }

    [Fact]
    public async Task Unknown_user_owner_takes_its_name_and_upn_from_the_owners_read()
    {
        var finding = Assert.Single(await RunAsync(Reader(
            [Assign(PrivilegedRoles.GlobalAdministrator, RoleGroupId)],
            Owners(UserOwner(9, "New user", "new@contoso.com")))));

        Assert.Equal(("User", "New user", "new@contoso.com"),
            (finding.SubObjectType, finding.SubObjectDisplayName, finding.UserPrincipalName));
    }

    [Fact]
    public async Task Known_user_owner_takes_its_name_and_upn_from_the_user_list()
    {
        var finding = Assert.Single(await RunAsync(Reader(
            [Assign(PrivilegedRoles.GlobalAdministrator, RoleGroupId)],
            Owners(UserOwner(1, "Old name", "old@contoso.com")))));

        Assert.Equal(("User 1", "user1@contoso.com"), (finding.SubObjectDisplayName, finding.UserPrincipalName));
    }

    [Theory]
    [InlineData(PrivilegedRoles.GlobalAdministrator)]
    [InlineData(PrivilegedRoles.PrivilegedRoleAdministrator)]
    public async Task Owner_with_a_direct_group_manager_role_is_skipped(string role)
    {
        var findings = await RunAsync(Reader(
            [Assign(PrivilegedRoles.GlobalAdministrator, RoleGroupId), Assign(role, UserId(1))],
            Owners(UserOwner(1), UserOwner(2))));

        Assert.Equal(["user2@contoso.com"], findings.Select(f => f.UserPrincipalName));
    }

    [Fact]
    public async Task Owner_holding_global_administrator_only_through_this_group_is_reported()
    {
        var reader = new FakeDirectoryReader(apps: [], groups: [Group(RoleGroupId)])
        {
            Users = [User(1)],
            RoleAssignments = [Assign(PrivilegedRoles.GlobalAdministrator, RoleGroupId)],
            RoleDefinitions = Definitions,
            GroupOwners = Owners(UserOwner(1)),
            GroupMembers = new Dictionary<string, IReadOnlyList<string>> { [RoleGroupId] = [UserId(1)] },
        };

        Assert.Single(await RunAsync(reader));
    }

    [Fact]
    public async Task Owner_holding_global_administrator_through_another_group_is_reported()
    {
        var reader = new FakeDirectoryReader(apps: [], groups: [Group(RoleGroupId), Group(OtherGroupId)])
        {
            Users = [User(1)],
            RoleAssignments =
            [
                Assign(SecurityAdministrator, RoleGroupId), Assign(PrivilegedRoles.GlobalAdministrator, OtherGroupId),
            ],
            RoleDefinitions = Definitions,
            GroupOwners = Owners(UserOwner(1)),
            GroupMembers = new Dictionary<string, IReadOnlyList<string>> { [OtherGroupId] = [UserId(1)] },
        };

        var finding = Assert.Single(await RunAsync(reader));

        Assert.Equal((RoleGroupId, "user1@contoso.com"), (finding.ObjectId, finding.UserPrincipalName));
    }

    [Fact]
    public async Task Disabled_guest_and_synced_user_owners_are_reported()
    {
        var findings = await RunAsync(Reader(
            [Assign(PrivilegedRoles.GlobalAdministrator, RoleGroupId)],
            Owners(UserOwner(1), UserOwner(2), UserOwner(3)),
            users: [User(1, enabled: false), User(2, type: UserType.Guest), User(3, synced: true)]));

        Assert.Equal(3, findings.Count);
    }

    [Fact]
    public async Task Built_in_roles_match_on_definition_id_or_template_id()
    {
        const string GlobalAdministratorDefinitionId = "dddddddd-0000-0000-0000-000000000001";
        const string PrivilegedRoleAdministratorDefinitionId = "dddddddd-0000-0000-0000-000000000002";
        var definitions = new DirectoryRoleDefinition[]
        {
            new() { Id = GlobalAdministratorDefinitionId, TemplateId = PrivilegedRoles.GlobalAdministrator, DisplayName = "Global Administrator" },
            new() { Id = PrivilegedRoleAdministratorDefinitionId, TemplateId = PrivilegedRoles.PrivilegedRoleAdministrator, DisplayName = null },
        };

        var finding = Assert.Single(await RunAsync(Reader(
            [
                Assign(GlobalAdministratorDefinitionId, RoleGroupId),
                Assign(PrivilegedRoleAdministratorDefinitionId, UserId(1)),
            ],
            Owners(UserOwner(1), UserOwner(2)),
            definitions: definitions)));

        Assert.Equal((Severity.Critical, "Global Administrator", "user2@contoso.com"),
            (finding.Severity, finding.Value, finding.UserPrincipalName));
    }

    [Fact]
    public async Task Fails_on_its_own_when_reading_owners_fails()
    {
        var reader = new FakeDirectoryReader(apps: [], groups: [Group(RoleGroupId)])
        {
            RoleAssignments = [Assign(PrivilegedRoles.GlobalAdministrator, RoleGroupId)],
            RoleDefinitions = Definitions,
            GroupOwnersFailure = new DirectoryReadException("denied", 403),
        };

        await Assert.ThrowsAsync<DirectoryReadException>(() => RunAsync(reader));
    }
}
