using System.Collections.Generic;
using System.Linq;
using Entraspect.Core.Checks;

namespace Entraspect.Core.Tests.Checks;

public sealed class GuestsAdminRolesCheckTests
{
    private const string GuestId = "aaaaaaaa-0000-0000-0000-000000000001";
    private const string OtherGuestId = "aaaaaaaa-0000-0000-0000-000000000002";
    private const string MemberId = "aaaaaaaa-0000-0000-0000-000000000003";
    private const string RoleGroupId = "99999999-0000-0000-0000-000000000001";
    private const string ServicePrincipalId = "bbbbbbbb-0000-0000-0000-000000000001";
    private const string GlobalReader = "f2ef992c-3afb-46b9-b7cf-a126ee74c451";
    private const string CustomRoleId = "cccccccc-0000-0000-0000-000000000001";

    private static DirectoryUser User(string id, UserType type = UserType.Guest, bool enabled = true) =>
        new()
        {
            Id = id,
            UserPrincipalName = $"{id}@contoso.com",
            DisplayName = $"User {id[^1]}",
            UserType = type,
            AccountEnabled = enabled,
        };

    private static readonly DirectoryUser[] Users =
    [
        User(GuestId),
        User(OtherGuestId),
        User(MemberId, UserType.Member),
    ];

    private static readonly DirectoryRoleDefinition[] Definitions =
    [
        new() { Id = GlobalReader, TemplateId = GlobalReader, DisplayName = "Global Reader" },
        new() { Id = CustomRoleId, TemplateId = null, DisplayName = "Helpdesk Lite" },
    ];

    private static FakeDirectoryReader Reader(
        DirectoryRoleAssignment[] assignments,
        DirectoryUser[]? users = null,
        Dictionary<string, IReadOnlyList<string>>? groups = null,
        DirectoryRoleDefinition[]? definitions = null) =>
        new(apps: [])
        {
            Users = users ?? Users,
            RoleAssignments = assignments,
            RoleDefinitions = definitions ?? Definitions,
            GroupMembers = groups ?? new Dictionary<string, IReadOnlyList<string>>(),
        };

    private static DirectoryRoleAssignment Assign(string role, string principal) =>
        new() { RoleDefinitionId = role, PrincipalId = principal };

    private static Task<IReadOnlyList<Finding>> RunAsync(FakeDirectoryReader reader) =>
        new GuestsAdminRolesCheck().RunAsync(reader, new CheckOptions());

    [Fact]
    public async Task Guest_with_a_direct_assignment_is_critical()
    {
        var finding = Assert.Single(await RunAsync(Reader([Assign(GlobalReader, GuestId)])));

        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal(
            "Guest holds the Global Reader role. Their identity is managed by another organisation.",
            finding.Message);
        Assert.Equal("Global Reader", finding.SubObjectDisplayName);
        Assert.Equal(("guests-admin-roles", "User", GuestId), (finding.CheckId, finding.ObjectType, finding.ObjectId));
        Assert.Equal($"{GuestId}@contoso.com", finding.UserPrincipalName);
        Assert.Null(finding.DueDate);
    }

    [Fact]
    public async Task Guest_holding_a_role_through_a_group_is_critical()
    {
        var reader = Reader(
            [Assign(GlobalReader, RoleGroupId)],
            groups: new() { [RoleGroupId] = [MemberId, OtherGuestId] });

        var finding = Assert.Single(await RunAsync(reader));

        Assert.Equal(OtherGuestId, finding.ObjectId);
        Assert.Equal("Global Reader", finding.SubObjectDisplayName);
        Assert.Equal([RoleGroupId], reader.GroupMemberReads);
    }

    [Fact]
    public async Task Custom_role_is_named()
    {
        var finding = Assert.Single(await RunAsync(Reader([Assign(CustomRoleId, GuestId)])));

        Assert.Equal("Helpdesk Lite", finding.SubObjectDisplayName);
        Assert.Contains("the Helpdesk Lite role", finding.Message);
    }

    [Fact]
    public async Task Built_in_role_matches_on_template_id()
    {
        var definitions = new DirectoryRoleDefinition[]
        {
            new() { Id = "dddddddd-0000-0000-0000-000000000001", TemplateId = GlobalReader, DisplayName = "Global Reader" },
        };

        var finding = Assert.Single(await RunAsync(Reader([Assign(GlobalReader, GuestId)], definitions: definitions)));

        Assert.Equal("Global Reader", finding.SubObjectDisplayName);
    }

    [Fact]
    public async Task Missing_role_name_falls_back_to_the_role_definition_id()
    {
        var definitions = new DirectoryRoleDefinition[] { new() { Id = CustomRoleId, DisplayName = null } };

        var finding = Assert.Single(await RunAsync(Reader([Assign(CustomRoleId, GuestId)], definitions: definitions)));

        Assert.Equal(CustomRoleId, finding.SubObjectDisplayName);
        Assert.Contains($"the {CustomRoleId} role", finding.Message);
    }

    [Fact]
    public async Task One_finding_per_guest_and_role()
    {
        var reader = Reader(
            [Assign(GlobalReader, GuestId), Assign(GlobalReader, RoleGroupId), Assign(CustomRoleId, GuestId)],
            groups: new() { [RoleGroupId] = [GuestId.ToUpperInvariant()] });

        var findings = await RunAsync(reader);

        Assert.Equal(
            new[] { "Global Reader", "Helpdesk Lite" },
            findings.Select(f => f.SubObjectDisplayName).Order());
        Assert.All(findings, f => Assert.Equal(GuestId, f.ObjectId));
    }

    [Fact]
    public async Task Members_and_service_principals_are_ignored()
    {
        var reader = Reader([Assign(GlobalReader, MemberId), Assign(GlobalReader, ServicePrincipalId)]);

        Assert.Empty(await RunAsync(reader));
        Assert.Equal([ServicePrincipalId], reader.GroupMemberReads);
    }

    [Fact]
    public async Task Disabled_guests_are_reported_unchanged()
    {
        var finding = Assert.Single(await RunAsync(
            Reader([Assign(GlobalReader, GuestId)], users: [User(GuestId, enabled: false)])));

        Assert.Equal(Severity.Critical, finding.Severity);
    }

    [Fact]
    public async Task Tenant_without_guests_reads_no_groups()
    {
        var reader = Reader([Assign(GlobalReader, RoleGroupId)], users: [User(MemberId, UserType.Member)]);

        Assert.Empty(await RunAsync(reader));
        Assert.Empty(reader.GroupMemberReads);
    }

    [Fact]
    public async Task Fails_when_role_definitions_cannot_be_read()
    {
        var reader = new FakeDirectoryReader(apps: [])
        {
            Users = Users,
            RoleAssignments = [Assign(GlobalReader, GuestId)],
            RoleDefinitionsFailure = new DirectoryReadException("denied", 403),
        };

        await Assert.ThrowsAsync<DirectoryReadException>(() => RunAsync(reader));
    }
}
