using System.Collections.Generic;
using Entraspect.Core.Checks;
using Microsoft.Extensions.Time.Testing;

namespace Entraspect.Core.Tests.Checks;

public sealed class GuestsPendingInvitesCheckTests
{
    private const string GuestId = "aaaaaaaa-0000-0000-0000-000000000001";
    private const string Advice = " Resend it, or delete the guest if it's no longer needed.";
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Now);

    private static DirectoryUser Guest(
        ExternalUserState? state = ExternalUserState.PendingAcceptance,
        DateTimeOffset? changed = null,
        DateTimeOffset? created = null,
        bool enabled = true,
        UserType type = UserType.Guest) =>
        new()
        {
            Id = GuestId,
            UserPrincipalName = "guest_fabrikam.com#EXT#@contoso.onmicrosoft.com",
            DisplayName = "Guest",
            UserType = type,
            AccountEnabled = enabled,
            ExternalUserState = state,
            ExternalUserStateChangeDateTime = changed,
            CreatedDateTime = created,
        };

    private Task<IReadOnlyList<Finding>> RunAsync(DirectoryUser user, CheckOptions? options = null) =>
        new GuestsPendingInvitesCheck(_time).RunAsync(
            new FakeDirectoryReader(apps: []) { Users = [user] },
            options ?? new CheckOptions());

    [Fact]
    public async Task Invitation_pending_exactly_the_setting_is_info()
    {
        var invited = Now.AddDays(-30);

        var finding = Assert.Single(await RunAsync(Guest(changed: invited, created: Now.AddDays(-400))));

        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Equal("Invitation not accepted after 30 days." + Advice, finding.Message);
        Assert.Equal(invited, finding.DueDate);
        Assert.Equal(("guests-pending-invites", "User", GuestId), (finding.CheckId, finding.ObjectType, finding.ObjectId));
        Assert.Equal("guest_fabrikam.com#EXT#@contoso.onmicrosoft.com", finding.UserPrincipalName);
        Assert.Null(finding.Value);
    }

    [Fact]
    public async Task Invitation_pending_less_than_the_setting_is_not_reported()
    {
        Assert.Empty(await RunAsync(Guest(changed: Now.AddDays(-29))));
    }

    [Fact]
    public async Task Falls_back_to_the_creation_date()
    {
        var created = Now.AddDays(-45);

        var finding = Assert.Single(await RunAsync(Guest(created: created)));

        Assert.Equal("Invitation not accepted after 45 days." + Advice, finding.Message);
        Assert.Equal(created, finding.DueDate);
    }

    [Fact]
    public async Task Without_any_date_is_reported_without_a_due_date()
    {
        var finding = Assert.Single(await RunAsync(Guest()));

        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Equal("Invitation not accepted." + Advice, finding.Message);
        Assert.Null(finding.DueDate);
    }

    [Theory]
    [InlineData(ExternalUserState.Accepted)]
    [InlineData(ExternalUserState.Unknown)]
    [InlineData(null)]
    public async Task Other_invitation_states_are_not_reported(ExternalUserState? state)
    {
        Assert.Empty(await RunAsync(Guest(state, changed: Now.AddDays(-400))));
    }

    [Fact]
    public async Task Members_are_ignored()
    {
        Assert.Empty(await RunAsync(Guest(changed: Now.AddDays(-400), type: UserType.Member)));
    }

    [Fact]
    public async Task Disabled_guests_are_reported_unchanged()
    {
        var finding = Assert.Single(await RunAsync(Guest(changed: Now.AddDays(-40), enabled: false)));

        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Equal("Invitation not accepted after 40 days." + Advice, finding.Message);
    }

    [Fact]
    public async Task Uses_the_pending_invite_days_setting()
    {
        var finding = Assert.Single(await RunAsync(
            Guest(changed: Now.AddDays(-1)),
            new CheckOptions { GuestPendingInviteDays = 1 }));

        Assert.Equal("Invitation not accepted after 1 day." + Advice, finding.Message);
    }

    [Fact]
    public async Task Fails_when_users_cannot_be_read()
    {
        var reader = new FakeDirectoryReader(apps: []) { UsersFailure = new DirectoryReadException("denied", 403) };

        await Assert.ThrowsAsync<DirectoryReadException>(
            () => new GuestsPendingInvitesCheck(_time).RunAsync(reader, new CheckOptions()));
    }
}
