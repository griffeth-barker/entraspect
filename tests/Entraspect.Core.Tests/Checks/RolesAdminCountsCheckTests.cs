using System.Collections.Generic;
using System.Linq;
using Entraspect.Core.Checks;
using static Entraspect.Core.Tests.Checks.AdminTestData;

namespace Entraspect.Core.Tests.Checks;

public sealed class RolesAdminCountsCheckTests
{
    private static Task<IReadOnlyList<Finding>> RunAsync(
        IReadOnlyList<DirectoryRoleAssignment> assignments,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? groups = null,
        int users = 20) =>
        new RolesAdminCountsCheck().RunAsync(
            Reader(Enumerable.Range(1, users).Select(n => User(n)).ToList(), assignments, groups),
            new CheckOptions());

    private static DirectoryRoleAssignment[] GlobalAdmins(int count) =>
        Enumerable.Range(1, count).Select(n => Assign(PrivilegedRoles.GlobalAdministrator, UserId(n))).ToArray();

    [Theory]
    [InlineData(0, "Only 0 users hold Global Administrator.")]
    [InlineData(1, "Only 1 user holds Global Administrator.")]
    public async Task Fewer_than_two_global_administrators_is_a_warning(int count, string start)
    {
        var finding = Assert.Single(await RunAsync(GlobalAdmins(count)));

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal(
            start + " Keep at least two, such as two emergency access accounts, so you can't lose access to the tenant.",
            finding.Message);
        Assert.Equal(("roles-admin-counts", "Role", PrivilegedRoles.GlobalAdministrator, "Global Administrator"),
            (finding.CheckId, finding.ObjectType, finding.ObjectId, finding.ObjectDisplayName));
        Assert.Equal(count.ToString(), finding.Value);
        Assert.Null(finding.UserPrincipalName);
        Assert.Null(finding.SubObjectDisplayName);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public async Task Two_to_four_global_administrators_is_fine(int count)
    {
        Assert.Empty(await RunAsync(GlobalAdmins(count)));
    }

    [Fact]
    public async Task Five_global_administrators_is_a_warning()
    {
        var finding = Assert.Single(await RunAsync(GlobalAdmins(5)));

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("5 users hold Global Administrator. Microsoft recommends fewer than five.", finding.Message);
        Assert.Equal("5", finding.Value);
    }

    [Fact]
    public async Task Global_administrators_through_a_group_count_once_per_user()
    {
        var findings = await RunAsync(
            [
                Assign(PrivilegedRoles.GlobalAdministrator, RoleGroupId),
                Assign(PrivilegedRoles.GlobalAdministrator, UserId(1)),
            ],
            new Dictionary<string, IReadOnlyList<string>> { [RoleGroupId] = [UserId(1), UserId(2), UserId(3)] });

        Assert.Empty(findings);
    }

    [Fact]
    public async Task A_global_administrator_definition_named_by_its_own_id_counts()
    {
        const string DefinitionId = "11111111-0000-0000-0000-000000000001";
        var withDefinition = new FakeDirectoryReader(apps: [])
        {
            Users = [User(1), User(2)],
            RoleAssignments = [Assign(DefinitionId, UserId(1)), Assign(PrivilegedRoles.GlobalAdministrator, UserId(2))],
            RoleDefinitions =
            [
                new() { Id = DefinitionId, TemplateId = PrivilegedRoles.GlobalAdministrator, DisplayName = "Global Administrator" },
            ],
        };

        Assert.Empty(await new RolesAdminCountsCheck().RunAsync(withDefinition, new CheckOptions()));
    }

    [Theory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    public async Task Ten_privileged_role_assignments_is_info(int assignments, bool reported)
    {
        // Two Global Administrators, then privileged roles that aren't Global Administrator up to the total, plus a
        // role that isn't privileged, which doesn't count.
        var list = GlobalAdmins(2)
            .Concat(Enumerable.Range(3, assignments - 2).Select(n => Assign(GlobalReader, UserId(n))))
            .Append(Assign(DirectoryReaders, UserId(1)))
            .ToArray();

        var finding = (await RunAsync(list)).SingleOrDefault();

        if (!reported)
        {
            Assert.Null(finding);
            return;
        }

        Assert.NotNull(finding);
        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Equal("10 privileged role assignments. Microsoft recommends fewer than ten.", finding.Message);
        Assert.Equal(("Role", string.Empty, "Privileged roles", "10"),
            (finding.ObjectType, finding.ObjectId, finding.ObjectDisplayName, finding.Value));
    }

    [Fact]
    public async Task Privileged_assignments_are_distinct_user_and_role_pairs()
    {
        // Five users reach both roles directly and through the group: ten pairs, not twenty.
        var members = Enumerable.Range(1, 5).Select(UserId).ToList();
        var list = new[] { Assign(PrivilegedRoles.GlobalAdministrator, RoleGroupId), Assign(GlobalReader, RoleGroupId) }
            .Concat(members.SelectMany(id => new[] { Assign(PrivilegedRoles.GlobalAdministrator, id), Assign(GlobalReader, id) }))
            .ToArray();

        var findings = await RunAsync(list, new Dictionary<string, IReadOnlyList<string>> { [RoleGroupId] = members });

        Assert.Equal(["5", "10"], findings.Select(f => f.Value));
    }

    [Fact]
    public async Task Fails_on_its_own_when_a_read_fails()
    {
        var reader = new FakeDirectoryReader(apps: [])
        {
            RoleAssignmentsFailure = new DirectoryReadException("denied", 403),
        };

        await Assert.ThrowsAsync<DirectoryReadException>(
            () => new RolesAdminCountsCheck().RunAsync(reader, new CheckOptions()));
    }
}
