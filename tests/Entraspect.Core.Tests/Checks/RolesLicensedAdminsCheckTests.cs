using System.Collections.Generic;
using Entraspect.Core.Checks;
using static Entraspect.Core.Tests.Checks.AdminTestData;

namespace Entraspect.Core.Tests.Checks;

public sealed class RolesLicensedAdminsCheckTests
{
    private static Task<IReadOnlyList<Finding>> RunAsync(DirectoryUser user, string role) =>
        new RolesLicensedAdminsCheck().RunAsync(Reader([user], [Assign(role, user.Id)]), new CheckOptions());

    [Theory]
    [InlineData(1, "1 licence")]
    [InlineData(3, "3 licences")]
    public async Task Licensed_admins_are_info(int licences, string text)
    {
        var finding = Assert.Single(await RunAsync(User(1, licences: licences), PrivilegedRoles.GlobalAdministrator));

        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Equal(
            $"Admin account has {text} assigned. Use a separate, unlicensed account for admin work, so email and " +
            "everyday apps aren't used with admin rights.",
            finding.Message);
        Assert.Equal(licences.ToString(), finding.Value);
        Assert.Equal("Global Administrator", finding.SubObjectDisplayName);
        Assert.Equal(("roles-licensed-admins", "User", UserId(1)), (finding.CheckId, finding.ObjectType, finding.ObjectId));
    }

    [Fact]
    public async Task Privileged_admins_are_info_too()
    {
        Assert.Equal(Severity.Info, Assert.Single(await RunAsync(User(1, licences: 2), GlobalReader)).Severity);
    }

    [Fact]
    public async Task Unlicensed_admins_are_not_reported()
    {
        Assert.Empty(await RunAsync(User(1, licences: 0), PrivilegedRoles.GlobalAdministrator));
    }

    [Fact]
    public async Task Licensed_users_without_a_privileged_role_are_not_reported()
    {
        Assert.Empty(await RunAsync(User(1, licences: 2), DirectoryReaders));
    }

    [Fact]
    public async Task Fails_on_its_own_when_a_read_fails()
    {
        var reader = new FakeDirectoryReader(apps: []) { RoleDefinitionsFailure = new DirectoryReadException("denied", 403) };

        await Assert.ThrowsAsync<DirectoryReadException>(
            () => new RolesLicensedAdminsCheck().RunAsync(reader, new CheckOptions()));
    }
}
