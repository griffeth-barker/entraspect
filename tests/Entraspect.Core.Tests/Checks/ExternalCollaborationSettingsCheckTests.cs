using Entraspect.Core.Checks;

namespace Entraspect.Core.Tests.Checks;

public sealed class ExternalCollaborationSettingsCheckTests
{
    // Restricted guests, invitations from admins and Guest Inviters, no self-service join: nothing to report.
    private static AuthorizationPolicySettings Locked(
        string guestUserRoleId = ExternalCollaborationSettingsCheck.RestrictedGuestRoleId,
        AllowInvitesFrom allowInvitesFrom = AllowInvitesFrom.AdminsAndGuestInviters,
        string? allowInvitesFromRaw = "adminsAndGuestInviters",
        bool? allowEmailVerifiedUsersToJoin = false) => new()
    {
        GuestUserRoleId = guestUserRoleId,
        AllowInvitesFrom = allowInvitesFrom,
        AllowInvitesFromRaw = allowInvitesFromRaw,
        AllowEmailVerifiedUsersToJoinOrganization = allowEmailVerifiedUsersToJoin,
    };

    private static Task<IReadOnlyList<Finding>> RunAsync(AuthorizationPolicySettings policy) =>
        new ExternalCollaborationSettingsCheck().RunAsync(
            new FakeDirectoryReader(Array.Empty<AppRegistration>()) { AuthorizationPolicy = policy },
            new CheckOptions());

    [Fact]
    public async Task Locked_down_settings_have_no_finding()
    {
        Assert.Empty(await RunAsync(Locked()));
    }

    [Fact]
    public async Task Tenant_defaults_report_limited_guest_access_and_invitations_from_everyone()
    {
        var findings = await RunAsync(Locked(
            ExternalCollaborationSettingsCheck.GuestRoleId, AllowInvitesFrom.Everyone, "everyone"));

        Assert.Equal(2, findings.Count);
        var access = findings[0];
        Assert.Equal("external-collaboration-settings", access.CheckId);
        Assert.Equal(Severity.Info, access.Severity);
        Assert.Equal("AuthorizationPolicy", access.ObjectType);
        Assert.Equal("guestUserRoleId", access.ObjectId);
        Assert.Equal("Guest user access", access.ObjectDisplayName);
        Assert.Equal("Limited", access.Value);
        Assert.Equal(
            "Guests can see other users and group memberships. Consider restricting them to their own directory objects.",
            access.Message);

        var invite = findings[1];
        Assert.Equal(Severity.Warning, invite.Severity);
        Assert.Equal("allowInvitesFrom", invite.ObjectId);
        Assert.Equal("Guest invite restrictions", invite.ObjectDisplayName);
        Assert.Equal("Everyone", invite.Value);
        Assert.Equal(
            "Anyone in the organisation, guests included, can invite guests. Limit invitations to admins and the " +
            "Guest Inviter role.",
            invite.Message);
    }

    [Fact]
    public async Task Guests_with_member_access_are_critical()
    {
        var f = Assert.Single(await RunAsync(Locked(ExternalCollaborationSettingsCheck.MemberRoleId)));

        Assert.Equal(Severity.Critical, f.Severity);
        Assert.Equal("Same as members", f.Value);
        Assert.Equal("Guests have the same access to directory data as members. Limit guest access.", f.Message);
    }

    [Fact]
    public async Task Role_ids_match_whatever_their_case()
    {
        Assert.Empty(await RunAsync(Locked(ExternalCollaborationSettingsCheck.RestrictedGuestRoleId.ToUpperInvariant())));
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000001", "00000000-0000-0000-0000-000000000001")]
    [InlineData("not-a-guid", "not-a-guid")]
    [InlineData("", null)]
    public async Task Unrecognised_guest_access_is_info_with_the_raw_id(string roleId, string? value)
    {
        var f = Assert.Single(await RunAsync(Locked(roleId)));

        Assert.Equal(Severity.Info, f.Severity);
        Assert.Equal("guestUserRoleId", f.ObjectId);
        Assert.Equal(value, f.Value);
        Assert.Equal("Unrecognised guest access level.", f.Message);
    }

    [Fact]
    public async Task Invitations_from_every_member_are_info()
    {
        var f = Assert.Single(await RunAsync(Locked(
            allowInvitesFrom: AllowInvitesFrom.AdminsGuestInvitersAndAllMembers,
            allowInvitesFromRaw: "adminsGuestInvitersAndAllMembers")));

        Assert.Equal(Severity.Info, f.Severity);
        Assert.Equal("Members and admins", f.Value);
        Assert.Equal(
            "Every member can invite guests. Consider limiting invitations to admins and the Guest Inviter role.",
            f.Message);
    }

    [Fact]
    public async Task No_one_inviting_has_no_finding()
    {
        Assert.Empty(await RunAsync(Locked(allowInvitesFrom: AllowInvitesFrom.None, allowInvitesFromRaw: "none")));
    }

    [Theory]
    [InlineData("someNewValue", "someNewValue")]
    [InlineData(null, null)]
    public async Task Unknown_invite_setting_is_info_with_the_raw_value(string? raw, string? value)
    {
        var f = Assert.Single(await RunAsync(Locked(
            allowInvitesFrom: AllowInvitesFrom.Unknown, allowInvitesFromRaw: raw)));

        Assert.Equal(Severity.Info, f.Severity);
        Assert.Equal("allowInvitesFrom", f.ObjectId);
        Assert.Equal(value, f.Value);
        Assert.Equal("Unrecognised guest invite setting.", f.Message);
    }

    [Fact]
    public async Task Email_verified_join_is_a_warning()
    {
        var f = Assert.Single(await RunAsync(Locked(allowEmailVerifiedUsersToJoin: true)));

        Assert.Equal(Severity.Warning, f.Severity);
        Assert.Equal("allowEmailVerifiedUsersToJoinOrganization", f.ObjectId);
        Assert.Equal("Email-verified users can join", f.ObjectDisplayName);
        Assert.Equal("On", f.Value);
        Assert.Equal(
            "People can join this tenant by verifying an email address at one of its domains, without an invitation.",
            f.Message);
    }

    [Fact]
    public async Task Unknown_email_verified_join_has_no_finding()
    {
        Assert.Empty(await RunAsync(Locked(allowEmailVerifiedUsersToJoin: null)));
    }

    [Fact]
    public async Task Failed_read_throws_for_the_scan_to_report()
    {
        var reader = new FakeDirectoryReader(Array.Empty<AppRegistration>())
        {
            AuthorizationPolicyFailure = new DirectoryReadException("denied", 403),
        };

        await Assert.ThrowsAsync<DirectoryReadException>(
            () => new ExternalCollaborationSettingsCheck().RunAsync(reader, new CheckOptions()));
    }

    [Fact]
    public void Shows_setting_property_and_value_columns()
    {
        var check = new ExternalCollaborationSettingsCheck();

        Assert.Equal(CheckCategory.ExternalIdentities, check.Category);
        Assert.Equal(["Policy.Read.All"], check.RequiredPermissions);
        Assert.Equal(LicenceRequirement.None, ((ICheck)check).RequiredLicence);
        Assert.Equal("Setting", check.Labels.ObjectDisplayName);
        Assert.Equal("Graph property", check.Labels.ObjectId);
        Assert.Equal("Current value", check.Labels.Value);
        Assert.Null(check.Labels.UserPrincipalName);
        Assert.Null(check.Labels.SubObjectDisplayName);
        Assert.Null(check.Labels.SubObjectType);
        Assert.Null(check.Labels.DueDate);
    }
}
