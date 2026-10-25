using System.Collections.Generic;
using System.Linq;
using Entraspect.Core.Checks;
using static Entraspect.Core.Tests.Checks.AdminTestData;

namespace Entraspect.Core.Tests.Checks;

public sealed class AdminCheckRulesTests
{
    [Fact]
    public async Task Admins_hold_a_tier0_or_privileged_role_listed_highest_tier_first_then_by_name()
    {
        var reader = Reader(
            [User(1), User(2), User(3)],
            [
                Assign(DirectoryReaders, UserId(1)),
                Assign(SecurityAdministrator, UserId(1)),
                Assign(GlobalReader, UserId(1)),
                Assign(PrivilegedRoles.GlobalAdministrator, UserId(1)),
                Assign(DirectoryReaders, UserId(2)),
            ]);

        var admin = Assert.Single(await AdminCheckRules.GetAdminsAsync(reader, CancellationToken.None));

        Assert.Equal(UserId(1), admin.User.Id);
        Assert.Equal(RoleTier.Tier0, admin.Tier);
        Assert.Equal("Global Administrator, Global Reader, Security Administrator", admin.RolesText);
    }

    [Fact]
    public async Task Roles_through_a_group_count_once_and_guests_and_disabled_users_are_admins_too()
    {
        var reader = Reader(
            [User(1, type: UserType.Guest), User(2, enabled: false)],
            [
                Assign(GlobalReader, RoleGroupId),
                Assign(GlobalReader, UserId(1)),
            ],
            new Dictionary<string, IReadOnlyList<string>> { [RoleGroupId] = [UserId(1), UserId(2)] });

        var admins = await AdminCheckRules.GetAdminsAsync(reader, CancellationToken.None);

        Assert.Equal([UserId(1), UserId(2)], admins.Select(a => a.User.Id));
        Assert.All(admins, a => Assert.Equal("Global Reader", a.RolesText));
        Assert.All(admins, a => Assert.Equal(RoleTier.Privileged, a.Tier));
    }

    [Fact]
    public void Columns_are_the_user_columns_with_roles()
    {
        var labels = AdminCheckRules.Labels(value: "Licences");

        Assert.Equal(
            ("Display name", "User principal name", "Object ID", "Roles", "Licences"),
            (labels.ObjectDisplayName, labels.UserPrincipalName, labels.ObjectId, labels.SubObjectDisplayName,
                labels.Value));
        Assert.Null(labels.SubObjectType);
        Assert.Null(labels.DueDate);
    }
}
