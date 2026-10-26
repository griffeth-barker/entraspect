using System.Collections.Generic;
using System.Linq;
using Entraspect.Core.Checks;

namespace Entraspect.Core.Tests.Checks;

public sealed class RoleHoldersTests
{
    private const string UserId = "aaaaaaaa-0000-0000-0000-000000000001";
    private const string OtherUserId = "aaaaaaaa-0000-0000-0000-000000000002";
    private const string RoleGroupId = "99999999-0000-0000-0000-000000000001";
    private const string ServicePrincipalId = "bbbbbbbb-0000-0000-0000-000000000001";
    private const string GlobalReader = "f2ef992c-3afb-46b9-b7cf-a126ee74c451";
    private const string DirectoryReaders = "88d8e3e3-8f55-4a1e-953a-9b9898b8876b";
    private const string CustomRoleId = "cccccccc-0000-0000-0000-000000000001";

    private static DirectoryUser User(string id) =>
        new()
        {
            Id = id,
            UserPrincipalName = $"{id}@contoso.com",
            DisplayName = $"User {id[^1]}",
            UserType = UserType.Member,
            AccountEnabled = true,
        };

    private static readonly DirectoryUser[] Users = [User(UserId), User(OtherUserId)];

    private static readonly DirectoryRoleDefinition[] Definitions =
    [
        new() { Id = PrivilegedRoles.GlobalAdministrator, TemplateId = PrivilegedRoles.GlobalAdministrator, DisplayName = "Global Administrator" },
        new() { Id = GlobalReader, TemplateId = GlobalReader, DisplayName = "Global Reader" },
        new() { Id = DirectoryReaders, TemplateId = DirectoryReaders, DisplayName = "Directory Readers" },
        new() { Id = CustomRoleId, TemplateId = null, DisplayName = "Helpdesk Lite" },
    ];

    private static DirectoryRoleAssignment Assign(string role, string principal) =>
        new() { RoleDefinitionId = role, PrincipalId = principal };

    private static Task<IReadOnlyDictionary<string, IReadOnlyList<HeldRole>>> GetAsync(
        FakeDirectoryReader reader,
        DirectoryRoleAssignment[] assignments,
        DirectoryRoleDefinition[]? definitions = null) =>
        RoleHolders.GetAsync(Users, assignments, definitions ?? Definitions, reader);

    private static FakeDirectoryReader Reader(Dictionary<string, IReadOnlyList<string>>? groups = null) =>
        new(apps: []) { GroupMembers = groups ?? new Dictionary<string, IReadOnlyList<string>>() };

    [Fact]
    public async Task Direct_assignment_gives_the_role_with_its_name_and_tier()
    {
        var reader = Reader();

        var held = await GetAsync(reader, [Assign(PrivilegedRoles.GlobalAdministrator, UserId)]);

        var role = Assert.Single(Assert.Single(held).Value);
        Assert.Equal(new HeldRole(PrivilegedRoles.GlobalAdministrator, "Global Administrator", RoleTier.Tier0), role);
        Assert.Empty(reader.GroupMemberReads);
    }

    [Fact]
    public async Task Role_through_a_group_goes_to_each_member()
    {
        var reader = Reader(new() { [RoleGroupId] = [UserId, OtherUserId] });

        var held = await GetAsync(reader, [Assign(GlobalReader, RoleGroupId)]);

        Assert.Equal(GlobalReader, Assert.Single(held[UserId]).Id);
        Assert.Equal(RoleTier.Privileged, Assert.Single(held[OtherUserId]).Tier);
        Assert.Equal([RoleGroupId], reader.GroupMemberReads);
    }

    [Fact]
    public async Task Service_principals_are_read_as_groups_and_hold_nothing()
    {
        var reader = Reader();

        var held = await GetAsync(reader, [Assign(GlobalReader, ServicePrincipalId), Assign(GlobalReader, "not-an-id")]);

        Assert.Empty(held);
        Assert.Equal([ServicePrincipalId], reader.GroupMemberReads);
    }

    [Fact]
    public async Task Each_role_is_listed_once_per_user_whatever_the_case()
    {
        var reader = Reader(new() { [RoleGroupId] = [UserId.ToUpperInvariant()] });

        var held = await GetAsync(
            reader,
            [Assign(GlobalReader, UserId), Assign(GlobalReader.ToUpperInvariant(), RoleGroupId), Assign(GlobalReader, UserId)]);

        Assert.Equal(GlobalReader, Assert.Single(Assert.Single(held).Value).Id);
        Assert.True(held.ContainsKey(UserId.ToUpperInvariant()));
    }

    [Fact]
    public async Task Roles_are_ordered_highest_tier_first_then_by_name()
    {
        var held = await GetAsync(
            Reader(),
            [
                Assign(CustomRoleId, UserId),
                Assign(DirectoryReaders, UserId),
                Assign(GlobalReader, UserId),
                Assign(PrivilegedRoles.GlobalAdministrator, UserId),
            ]);

        Assert.Equal(
            ["Global Administrator", "Global Reader", "Directory Readers", "Helpdesk Lite"],
            held[UserId].Select(r => r.Name));
        Assert.Equal(
            [RoleTier.Tier0, RoleTier.Privileged, RoleTier.Other, RoleTier.Other],
            held[UserId].Select(r => r.Tier));
    }

    [Fact]
    public async Task Built_in_role_matches_on_template_id_for_its_name_and_tier()
    {
        const string DefinitionId = "dddddddd-0000-0000-0000-000000000001";
        var definitions = new DirectoryRoleDefinition[]
        {
            new() { Id = DefinitionId, TemplateId = GlobalReader, DisplayName = "Global Reader" },
        };

        var byTemplate = await GetAsync(Reader(), [Assign(GlobalReader, UserId)], definitions);
        var byDefinition = await GetAsync(Reader(), [Assign(DefinitionId, UserId)], definitions);

        Assert.Equal(new HeldRole(GlobalReader, "Global Reader", RoleTier.Privileged), Assert.Single(byTemplate[UserId]));
        Assert.Equal(new HeldRole(DefinitionId, "Global Reader", RoleTier.Privileged), Assert.Single(byDefinition[UserId]));
    }

    [Fact]
    public async Task Missing_role_name_falls_back_to_the_role_definition_id()
    {
        var definitions = new DirectoryRoleDefinition[] { new() { Id = CustomRoleId, DisplayName = null } };

        var held = await GetAsync(Reader(), [Assign(CustomRoleId, UserId), Assign(GlobalReader, UserId)], definitions);

        Assert.Equal(
            [new HeldRole(GlobalReader, GlobalReader, RoleTier.Privileged), new HeldRole(CustomRoleId, CustomRoleId, RoleTier.Other)],
            held[UserId]);
    }

    [Fact]
    public async Task Users_without_roles_are_left_out()
    {
        Assert.Empty(await GetAsync(Reader(), []));
    }

    [Fact]
    public void Principal_roles_stay_with_the_principal_and_group_members_are_not_read()
    {
        var held = RoleHolders.GetPrincipalRoles(
            [Assign(GlobalReader, RoleGroupId), Assign(PrivilegedRoles.GlobalAdministrator, UserId), Assign(GlobalReader, ServicePrincipalId)],
            Definitions);

        Assert.Equal(3, held.Count);
        Assert.Equal(new HeldRole(GlobalReader, "Global Reader", RoleTier.Privileged), Assert.Single(held[RoleGroupId]));
        Assert.Equal(PrivilegedRoles.GlobalAdministrator, Assert.Single(held[UserId]).Id);
        Assert.Equal(GlobalReader, Assert.Single(held[ServicePrincipalId]).Id);
    }

    [Fact]
    public void Principal_roles_are_distinct_whatever_the_case_and_ordered_highest_tier_first_then_by_name()
    {
        var held = RoleHolders.GetPrincipalRoles(
            [
                Assign(CustomRoleId, RoleGroupId),
                Assign(DirectoryReaders, RoleGroupId),
                Assign(GlobalReader, RoleGroupId.ToUpperInvariant()),
                Assign(GlobalReader.ToUpperInvariant(), RoleGroupId),
                Assign(PrivilegedRoles.GlobalAdministrator, RoleGroupId),
            ],
            Definitions);

        Assert.Equal(
            ["Global Administrator", "Global Reader", "Directory Readers", "Helpdesk Lite"],
            Assert.Single(held).Value.Select(r => r.Name));
        Assert.True(held.ContainsKey(RoleGroupId.ToUpperInvariant()));
    }

    [Fact]
    public void Principal_roles_match_built_in_roles_on_template_id_and_fall_back_to_the_id_without_a_name()
    {
        const string DefinitionId = "dddddddd-0000-0000-0000-000000000001";
        var definitions = new DirectoryRoleDefinition[]
        {
            new() { Id = DefinitionId, TemplateId = GlobalReader, DisplayName = "Global Reader" },
            new() { Id = CustomRoleId, DisplayName = null },
        };

        var held = RoleHolders.GetPrincipalRoles(
            [Assign(DefinitionId, RoleGroupId), Assign(GlobalReader, UserId), Assign(CustomRoleId, UserId)],
            definitions);

        Assert.Equal(new HeldRole(DefinitionId, "Global Reader", RoleTier.Privileged), Assert.Single(held[RoleGroupId]));
        Assert.Equal(
            [new HeldRole(GlobalReader, "Global Reader", RoleTier.Privileged), new HeldRole(CustomRoleId, CustomRoleId, RoleTier.Other)],
            held[UserId]);
    }

    [Fact]
    public void Principal_roles_are_empty_without_assignments()
    {
        Assert.Empty(RoleHolders.GetPrincipalRoles([], Definitions));
    }

    [Fact]
    public void Direct_holders_match_on_template_id_or_on_a_definition_with_that_template_id()
    {
        const string DefinitionId = "dddddddd-0000-0000-0000-000000000001";
        var definitions = new DirectoryRoleDefinition[]
        {
            new() { Id = DefinitionId, TemplateId = PrivilegedRoles.PrivilegedRoleAdministrator, DisplayName = null },
            new() { Id = GlobalReader, TemplateId = GlobalReader, DisplayName = "Global Reader" },
        };

        var holders = RoleHolders.GetDirectHolders(
            [
                Assign(PrivilegedRoles.GlobalAdministrator.ToUpperInvariant(), UserId),
                Assign(DefinitionId, OtherUserId),
                Assign(GlobalReader, ServicePrincipalId),
                Assign(CustomRoleId, RoleGroupId),
            ],
            definitions,
            [PrivilegedRoles.GlobalAdministrator, PrivilegedRoles.PrivilegedRoleAdministrator]);

        Assert.Equal(2, holders.Count);
        Assert.Contains(UserId.ToUpperInvariant(), holders);
        Assert.Contains(OtherUserId, holders);
    }
}
