using System.Collections.Generic;
using System.Linq;
using Entraspect.Core.Checks;
using Microsoft.Extensions.Time.Testing;

namespace Entraspect.Core.Tests.Checks;

public sealed class GuestsInactiveCheckTests
{
    private const string GuestId = "aaaaaaaa-0000-0000-0000-000000000001";
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Now);

    private static DirectoryUser Guest(
        string id = GuestId,
        bool enabled = true,
        DateTimeOffset? created = null,
        ExternalUserState? state = ExternalUserState.Accepted,
        UserType type = UserType.Guest) =>
        new()
        {
            Id = id,
            UserPrincipalName = $"{id}#EXT#@contoso.onmicrosoft.com",
            DisplayName = $"Guest {id[^1]}",
            UserType = type,
            AccountEnabled = enabled,
            CreatedDateTime = created ?? Now.AddDays(-400),
            ExternalUserState = state,
        };

    private static UserSignInActivity Activity(
        DateTimeOffset? success = null,
        DateTimeOffset? interactive = null,
        DateTimeOffset? nonInteractive = null,
        string id = GuestId) =>
        new()
        {
            UserId = id,
            LastSuccessfulSignIn = success,
            LastSignIn = interactive,
            LastNonInteractiveSignIn = nonInteractive,
        };

    private Task<IReadOnlyList<Finding>> RunAsync(
        DirectoryUser[] users,
        UserSignInActivity[]? activity = null,
        CheckOptions? options = null) =>
        new GuestsInactiveCheck(_time).RunAsync(
            new FakeDirectoryReader(apps: []) { Users = users, UserSignInActivity = activity ?? [] },
            options ?? new CheckOptions());

    [Fact]
    public async Task Successful_sign_in_exactly_the_setting_ago_is_a_warning()
    {
        var last = Now.AddDays(-90).AddHours(-1);

        var finding = Assert.Single(await RunAsync([Guest()], [Activity(success: last)]));

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("No successful sign-in for 90 days.", finding.Message);
        Assert.Equal(last, finding.DueDate);
        Assert.Equal(("guests-inactive", "User", GuestId), (finding.CheckId, finding.ObjectType, finding.ObjectId));
        Assert.Equal("Guest 1", finding.ObjectDisplayName);
        Assert.Equal($"{GuestId}#EXT#@contoso.onmicrosoft.com", finding.UserPrincipalName);
        Assert.Equal("Accepted", finding.Value);
    }

    [Fact]
    public async Task Successful_sign_in_one_day_more_than_the_setting_is_reported()
    {
        var finding = Assert.Single(await RunAsync([Guest()], [Activity(success: Now.AddDays(-91))]));

        Assert.Equal("No successful sign-in for 91 days.", finding.Message);
    }

    [Fact]
    public async Task Successful_sign_in_within_the_setting_is_not_reported()
    {
        Assert.Empty(await RunAsync([Guest()], [Activity(success: Now.AddDays(-89))]));
    }

    [Fact]
    public async Task Recent_successful_sign_in_wins_over_older_attempts()
    {
        var findings = await RunAsync(
            [Guest()],
            [Activity(success: Now.AddDays(-5), interactive: Now.AddDays(-200), nonInteractive: Now.AddDays(-300))]);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task Without_a_successful_sign_in_the_latest_attempt_counts()
    {
        var latest = Now.AddDays(-120);

        var finding = Assert.Single(await RunAsync(
            [Guest()],
            [Activity(interactive: Now.AddDays(-200), nonInteractive: latest)]));

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("No sign-in attempt for 120 days.", finding.Message);
        Assert.Equal(latest, finding.DueDate);
    }

    [Fact]
    public async Task A_recent_attempt_without_a_successful_sign_in_is_not_reported()
    {
        Assert.Empty(await RunAsync([Guest()], [Activity(interactive: Now.AddDays(-10))]));
    }

    [Fact]
    public async Task Never_signed_in_and_added_long_ago_is_a_warning()
    {
        var finding = Assert.Single(await RunAsync([Guest(created: Now.AddDays(-100))], [Activity()]));

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("Never signed in since being added 100 days ago.", finding.Message);
        Assert.Null(finding.DueDate);
    }

    [Fact]
    public async Task Guest_missing_from_the_sign_in_activity_never_signed_in()
    {
        var finding = Assert.Single(await RunAsync([Guest(created: Now.AddDays(-90))]));

        Assert.Equal("Never signed in since being added 90 days ago.", finding.Message);
    }

    [Fact]
    public async Task Never_signed_in_and_added_recently_is_not_reported()
    {
        Assert.Empty(await RunAsync([Guest(created: Now.AddDays(-89))], [Activity()]));
    }

    [Fact]
    public async Task Never_signed_in_with_unknown_creation_date_is_info()
    {
        var guest = new DirectoryUser
        {
            Id = GuestId,
            UserPrincipalName = "g@contoso.com",
            DisplayName = "Guest",
            UserType = UserType.Guest,
            AccountEnabled = true,
        };

        var finding = Assert.Single(await RunAsync([guest]));

        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Equal("Never signed in.", finding.Message);
        Assert.Null(finding.Value);
    }

    [Fact]
    public async Task Disabled_guest_is_info_with_a_note()
    {
        var finding = Assert.Single(await RunAsync([Guest(enabled: false)], [Activity(success: Now.AddDays(-200))]));

        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Equal(
            "No successful sign-in for 200 days. The account is disabled; consider deleting it.",
            finding.Message);
    }

    [Fact]
    public async Task Disabled_guest_that_is_not_inactive_is_not_reported()
    {
        Assert.Empty(await RunAsync([Guest(enabled: false)], [Activity(success: Now.AddDays(-1))]));
    }

    [Fact]
    public async Task Members_are_ignored()
    {
        Assert.Empty(await RunAsync([Guest(type: UserType.Member)], [Activity(success: Now.AddDays(-300))]));
    }

    [Fact]
    public async Task Pending_guest_shows_pending_invitation()
    {
        var finding = Assert.Single(await RunAsync([Guest(state: ExternalUserState.PendingAcceptance)]));

        Assert.Equal("Pending", finding.Value);
    }

    [Fact]
    public async Task Unknown_invitation_state_leaves_the_column_empty()
    {
        var finding = Assert.Single(await RunAsync([Guest(state: ExternalUserState.Unknown)]));

        Assert.Null(finding.Value);
    }

    [Fact]
    public async Task Uses_the_inactive_days_setting()
    {
        var options = new CheckOptions { GuestInactiveDays = 30 };

        var finding = Assert.Single(await RunAsync([Guest()], [Activity(success: Now.AddDays(-30))], options));

        Assert.Equal("No successful sign-in for 30 days.", finding.Message);
    }

    [Fact]
    public async Task Counts_utc_calendar_days()
    {
        // 23:59 UTC 90 calendar days ago is 89.5 days ago, but 90 calendar days.
        var last = new DateTimeOffset(Now.UtcDateTime.Date.AddDays(-90).AddHours(23).AddMinutes(59), TimeSpan.Zero);

        var finding = Assert.Single(await RunAsync([Guest()], [Activity(success: last)]));

        Assert.Equal("No successful sign-in for 90 days.", finding.Message);
    }

    [Fact]
    public async Task Fails_when_sign_in_activity_cannot_be_read()
    {
        var reader = new FakeDirectoryReader(apps: [])
        {
            Users = [Guest()],
            UserSignInActivityFailure = new DirectoryReadException("licence", 403),
        };

        await Assert.ThrowsAsync<DirectoryReadException>(
            () => new GuestsInactiveCheck(_time).RunAsync(reader, new CheckOptions()));
    }

    [Fact]
    public async Task A_sign_in_activity_failure_fails_only_this_check()
    {
        var reader = new FakeDirectoryReader(apps: [])
        {
            Users = [Guest(state: ExternalUserState.PendingAcceptance)],
            UserSignInActivityFailure = new DirectoryReadException("licence", 403),
        };
        var checks = new ICheck[] { new GuestsInactiveCheck(_time), new GuestsPendingInvitesCheck(_time) };
        var granted = new[] { "User.Read.All", "AuditLog.Read.All" };

        var results = await new ScanRunner(reader, checks, granted, new CheckOptions(), _time).RunAsync();

        Assert.Equal(CheckResultStatus.Failed, results[0].Status);
        Assert.Equal(CheckResultStatus.Succeeded, results[1].Status);
    }
}
