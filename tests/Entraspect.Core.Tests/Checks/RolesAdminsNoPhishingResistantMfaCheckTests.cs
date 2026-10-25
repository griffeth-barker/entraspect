using System.Collections.Generic;
using Entraspect.Core.Checks;
using static Entraspect.Core.Tests.Checks.AdminTestData;

namespace Entraspect.Core.Tests.Checks;

public sealed class RolesAdminsNoPhishingResistantMfaCheckTests
{
    private const string Message =
        "No phishing-resistant method registered (passkey, security key, Windows Hello or certificate). Admins should " +
        "sign in with one.";

    private static UserRegistration Registration(int n, params string[] methods) =>
        new()
        {
            Id = UserId(n),
            UserPrincipalName = $"user{n}@contoso.com",
            DisplayName = $"User {n}",
            UserType = UserType.Member,
            IsAdmin = true,
            IsMfaRegistered = methods.Length > 0,
            IsMfaCapable = methods.Length > 0,
            MethodsRegistered = methods,
            LastUpdatedDateTime = new DateTimeOffset(2026, 10, 24, n, 0, 0, TimeSpan.Zero),
        };

    private static Task<IReadOnlyList<Finding>> RunAsync(
        DirectoryUser user,
        string role,
        params UserRegistration[] registrations) =>
        new RolesAdminsNoPhishingResistantMfaCheck().RunAsync(
            Reader([user], [Assign(role, user.Id)], registrations: registrations),
            new CheckOptions());

    [Theory]
    [InlineData("passKeyDeviceBound")]
    [InlineData("passKeyDeviceBoundAuthenticator")]
    [InlineData("passKeyDeviceBoundWindowsHello")]
    [InlineData("fido2SecurityKey")]
    [InlineData("fido2")]
    [InlineData("windowsHelloForBusiness")]
    [InlineData("macOsSecureEnclaveKey")]
    [InlineData("certificateBasedAuthentication")]
    [InlineData("FIDO2SECURITYKEY")]
    [InlineData("WindowsHelloForBusiness")]
    public async Task Admins_with_a_phishing_resistant_method_are_not_reported(string method)
    {
        Assert.Empty(await RunAsync(
            User(1),
            PrivilegedRoles.GlobalAdministrator,
            Registration(1, "mobilePhone", method)));
    }

    [Theory]
    [InlineData(PrivilegedRoles.GlobalAdministrator, Severity.Warning, "Global Administrator")]
    [InlineData(SecurityAdministrator, Severity.Info, "Security Administrator")]
    public async Task Admins_without_one_are_reported_by_their_highest_tier(string role, Severity expected, string roles)
    {
        var finding = Assert.Single(await RunAsync(
            User(1),
            role,
            Registration(1, "microsoftAuthenticatorPush", "softwareOneTimePasscode", "somethingNew")));

        Assert.Equal(expected, finding.Severity);
        Assert.Equal(Message, finding.Message);
        Assert.Equal(roles, finding.SubObjectDisplayName);
        Assert.Equal(("roles-admins-no-phishing-resistant-mfa", "User", UserId(1), "user1@contoso.com"),
            (finding.CheckId, finding.ObjectType, finding.ObjectId, finding.UserPrincipalName));
    }

    [Fact]
    public async Task Admins_with_no_methods_are_reported()
    {
        Assert.Single(await RunAsync(User(1), PrivilegedRoles.GlobalAdministrator, Registration(1)));
    }

    [Theory]
    [InlineData(PrivilegedRoles.GlobalAdministrator)]
    [InlineData(GlobalReader)]
    public async Task An_admin_missing_from_the_report_is_info(string role)
    {
        var finding = Assert.Single(await RunAsync(User(1), role, Registration(2)));

        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Equal("No registration details for this admin.", finding.Message);
    }

    [Fact]
    public async Task Disabled_admins_are_skipped()
    {
        Assert.Empty(await RunAsync(User(1, enabled: false), PrivilegedRoles.GlobalAdministrator));
    }

    [Fact]
    public async Task Users_without_a_privileged_role_are_ignored()
    {
        Assert.Empty(await RunAsync(User(1), DirectoryReaders, Registration(1, "mobilePhone")));
    }

    [Fact]
    public async Task Data_updated_is_the_latest_report_update()
    {
        var reader = Reader([], [], registrations: [Registration(1), Registration(3), Registration(2)]);

        Assert.Equal(
            new DateTimeOffset(2026, 10, 24, 3, 0, 0, TimeSpan.Zero),
            await new RolesAdminsNoPhishingResistantMfaCheck().GetDataUpdatedAsync(reader));
    }

    [Fact]
    public async Task Fails_on_its_own_when_the_report_read_fails()
    {
        var reader = new FakeDirectoryReader(apps: [])
        {
            Users = [User(1)],
            RoleAssignments = [Assign(PrivilegedRoles.GlobalAdministrator, UserId(1))],
            RoleDefinitions = Definitions,
            UserRegistrationsFailure = new DirectoryReadException("licence", 403),
        };

        await Assert.ThrowsAsync<DirectoryReadException>(
            () => new RolesAdminsNoPhishingResistantMfaCheck().RunAsync(reader, new CheckOptions()));
    }
}
