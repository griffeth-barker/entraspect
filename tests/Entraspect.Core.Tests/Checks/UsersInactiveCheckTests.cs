using System.Collections.Generic;
using System.Linq;
using Entraspect.Core.Checks;
using Microsoft.Extensions.Time.Testing;

namespace Entraspect.Core.Tests.Checks;

public sealed class UsersInactiveCheckTests
{
    private const string UserId = "aaaaaaaa-0000-0000-0000-000000000001";
    private const string RoleGroupId = "99999999-0000-0000-0000-000000000001";
    private const string GlobalReader = "f2ef992c-3afb-46b9-b7cf-a126ee74c451";
    private const string UserAdministrator = "fe930be7-5e62-47db-91af-98c3a49a38b1";
    private const string DirectoryReaders = "88d8e3e3-8f55-4a1e-953a-9b9898b8876b";
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Now);

    private static readonly DirectoryRoleDefinition[] Definitions =
    [
        new() { Id = PrivilegedRoles.GlobalAdministrator, TemplateId = PrivilegedRoles.GlobalAdministrator, DisplayName = "Global Administrator" },
        new() { Id = GlobalReader, TemplateId = GlobalReader, DisplayName = "Global Reader" },
        new() { Id = UserAdministrator, TemplateId = UserAdministrator, DisplayName = "User Administrator" },
        new() { Id = DirectoryReaders, TemplateId = DirectoryReaders, DisplayName = "Directory Readers" },
    ];

    private static DirectoryUser Member(
        string id = UserId,
        bool enabled = true,
        DateTimeOffset? created = null,
        UserType type = UserType.Member,
        bool? synced = null) =>
        new()
        {
            Id = id,
            UserPrincipalName = $"{id}@contoso.com",
            DisplayName = $"User {id[^1]}",
            UserType = type,
            AccountEnabled = enabled,
            CreatedDateTime = created ?? Now.AddDays(-400),
            OnPremisesSyncEnabled = synced,
        };

    private static UserSignInActivity Activity(
        DateTimeOffset? success = null,
        DateTimeOffset? interactive = null,
        DateTimeOffset? nonInteractive = null,
        string id = UserId) =>
        new()
        {
            UserId = id,
            LastSuccessfulSignIn = success,
            LastSignIn = interactive,
            LastNonInteractiveSignIn = nonInteractive,
        };

    private static DirectoryRoleAssignment Assign(string role, string principal = UserId) =>
        new() { RoleDefinitionId = role, PrincipalId = principal };

    private static FakeDirectoryReader Reader(
        DirectoryUser[] users,
        UserSignInActivity[]? activity = null,
        DirectoryRoleAssignment[]? assignments = null,
        Dictionary<string, IReadOnlyList<string>>? groups = null) =>
        new(apps: [])
        {
            Users = users,
            UserSignInActivity = activity ?? [],
            RoleAssignments = assignments ?? [],
            RoleDefinitions = Definitions,
            GroupMembers = groups ?? new Dictionary<string, IReadOnlyList<string>>(),
        };

    private Task<IReadOnlyList<Finding>> RunAsync(FakeDirectoryReader reader, CheckOptions? options = null) =>
        new UsersInactiveCheck(_time).RunAsync(reader, options ?? new CheckOptions());

    private Task<IReadOnlyList<Finding>> RunAsync(
        DirectoryUser[] users,
        UserSignInActivity[]? activity = null,
        DirectoryRoleAssignment[]? assignments = null,
        CheckOptions? options = null) =>
        RunAsync(Reader(users, activity, assignments), options);

    [Fact]
    public async Task Successful_sign_in_exactly_the_setting_ago_is_a_warning()
    {
        var last = Now.AddDays(-90).AddHours(-1);

        var finding = Assert.Single(await RunAsync([Member()], [Activity(success: last)]));

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("No successful sign-in for 90 days.", finding.Message);
        Assert.Equal(last, finding.DueDate);
        Assert.Equal(("users-inactive", "User", UserId), (finding.CheckId, finding.ObjectType, finding.ObjectId));
        Assert.Equal("User 1", finding.ObjectDisplayName);
        Assert.Equal($"{UserId}@contoso.com", finding.UserPrincipalName);
        Assert.Equal("Cloud", finding.Value);
    }

    [Fact]
    public async Task Successful_sign_in_within_the_setting_is_not_reported()
    {
        Assert.Empty(await RunAsync([Member()], [Activity(success: Now.AddDays(-89))]));
    }

    [Fact]
    public async Task Days_count_utc_calendar_days()
    {
        // 89 days and 23 hours ago, but 90 calendar days in UTC.
        var last = new DateTimeOffset(2026, 7, 27, 23, 0, 0, TimeSpan.Zero);

        var finding = Assert.Single(await RunAsync([Member()], [Activity(success: last)]));

        Assert.Equal("No successful sign-in for 90 days.", finding.Message);
    }

    [Fact]
    public async Task Setting_changes_the_threshold()
    {
        var options = new CheckOptions { UserInactiveDays = 30 };

        var finding = Assert.Single(await RunAsync([Member()], [Activity(success: Now.AddDays(-30))], options: options));
        Assert.Equal("No successful sign-in for 30 days.", finding.Message);
        Assert.Empty(await RunAsync([Member()], [Activity(success: Now.AddDays(-29))], options: options));
    }

    [Fact]
    public async Task Guest_option_does_not_change_the_threshold()
    {
        var options = new CheckOptions { GuestInactiveDays = 30 };

        Assert.Empty(await RunAsync([Member()], [Activity(success: Now.AddDays(-60))], options: options));
    }

    [Fact]
    public async Task Without_a_successful_sign_in_the_later_attempt_counts()
    {
        var finding = Assert.Single(await RunAsync(
            [Member()],
            [Activity(interactive: Now.AddDays(-200), nonInteractive: Now.AddDays(-120))]));

        Assert.Equal("No sign-in attempt for 120 days.", finding.Message);
        Assert.Equal(Now.AddDays(-120), finding.DueDate);
    }

    [Fact]
    public async Task Recent_attempt_without_a_successful_sign_in_is_not_reported()
    {
        Assert.Empty(await RunAsync([Member()], [Activity(interactive: Now.AddDays(-200), nonInteractive: Now.AddDays(-5))]));
    }

    [Fact]
    public async Task Never_signed_in_and_created_long_ago_is_a_warning()
    {
        var finding = Assert.Single(await RunAsync([Member(created: Now.AddDays(-90))]));

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("Never signed in since being created 90 days ago.", finding.Message);
        Assert.Null(finding.DueDate);
    }

    [Fact]
    public async Task Never_signed_in_and_created_recently_is_not_reported()
    {
        Assert.Empty(await RunAsync([Member(created: Now.AddDays(-89))], [Activity()]));
    }

    [Fact]
    public async Task Never_signed_in_with_no_creation_date_is_info()
    {
        var user = new DirectoryUser
        {
            Id = UserId,
            UserPrincipalName = "u@contoso.com",
            DisplayName = "User",
            UserType = UserType.Member,
            AccountEnabled = true,
        };

        var finding = Assert.Single(await RunAsync([user]));

        Assert.Equal((Severity.Info, "Never signed in."), (finding.Severity, finding.Message));
    }

    [Fact]
    public async Task Tier_0_role_makes_it_critical()
    {
        var finding = Assert.Single(await RunAsync(
            [Member()],
            [Activity(success: Now.AddDays(-100))],
            [Assign(PrivilegedRoles.GlobalAdministrator)]));

        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal("No successful sign-in for 100 days. Holds Global Administrator.", finding.Message);
    }

    [Fact]
    public async Task Privileged_role_makes_it_critical()
    {
        var finding = Assert.Single(await RunAsync([Member()], [Activity(success: Now.AddDays(-100))], [Assign(GlobalReader)]));

        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.EndsWith(" Holds Global Reader.", finding.Message);
    }

    [Fact]
    public async Task Other_roles_do_not_change_the_severity()
    {
        var finding = Assert.Single(await RunAsync(
            [Member()],
            [Activity(success: Now.AddDays(-100))],
            [Assign(DirectoryReaders)]));

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("No successful sign-in for 100 days.", finding.Message);
    }

    [Fact]
    public async Task Names_the_highest_tier_role_and_counts_the_other_privileged_ones()
    {
        var oneMore = Assert.Single(await RunAsync(
            [Member()],
            [Activity(success: Now.AddDays(-100))],
            [Assign(GlobalReader), Assign(PrivilegedRoles.GlobalAdministrator), Assign(DirectoryReaders)]));
        var twoMore = Assert.Single(await RunAsync(
            [Member()],
            [Activity(success: Now.AddDays(-100))],
            [Assign(UserAdministrator), Assign(GlobalReader), Assign(PrivilegedRoles.GlobalAdministrator)]));

        Assert.EndsWith(" Holds Global Administrator and 1 more role.", oneMore.Message);
        Assert.EndsWith(" Holds Global Administrator and 2 more roles.", twoMore.Message);
    }

    [Fact]
    public async Task Role_through_a_group_counts()
    {
        var reader = Reader(
            [Member()],
            [Activity(success: Now.AddDays(-100))],
            [Assign(PrivilegedRoles.GlobalAdministrator, RoleGroupId)],
            new() { [RoleGroupId] = [UserId] });

        var finding = Assert.Single(await RunAsync(reader));

        Assert.Equal(Severity.Critical, finding.Severity);
    }

    [Fact]
    public async Task Never_signed_in_admin_is_critical()
    {
        var finding = Assert.Single(await RunAsync([Member()], assignments: [Assign(PrivilegedRoles.GlobalAdministrator)]));

        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal("Never signed in since being created 400 days ago. Holds Global Administrator.", finding.Message);
    }

    [Fact]
    public async Task Disabled_user_without_a_role_is_info()
    {
        var finding = Assert.Single(await RunAsync([Member(enabled: false)], [Activity(success: Now.AddDays(-100))]));

        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Equal(
            "No successful sign-in for 100 days. The account is disabled; consider deleting it.",
            finding.Message);
    }

    [Fact]
    public async Task Disabled_synced_user_says_it_is_managed_on_premises()
    {
        var finding = Assert.Single(await RunAsync(
            [Member(enabled: false, synced: true)],
            [Activity(success: Now.AddDays(-100))]));

        Assert.Equal(
            "No successful sign-in for 100 days. The account is disabled; consider deleting it. " +
            "This account is managed in on-premises Active Directory.",
            finding.Message);
        Assert.Equal("Synced", finding.Value);
    }

    [Theory]
    [InlineData(PrivilegedRoles.GlobalAdministrator)]
    [InlineData(DirectoryReaders)]
    public async Task Disabled_user_with_a_role_is_left_to_disabled_users_with_access(string role)
    {
        Assert.Empty(await RunAsync([Member(enabled: false)], [Activity(success: Now.AddDays(-100))], [Assign(role)]));
    }

    [Theory]
    [InlineData(true, "Synced")]
    [InlineData(false, "Cloud")]
    [InlineData(null, "Cloud")]
    public async Task Source_shows_synced_or_cloud(bool? synced, string expected)
    {
        var finding = Assert.Single(await RunAsync([Member(synced: synced)], [Activity(success: Now.AddDays(-100))]));

        Assert.Equal(expected, finding.Value);
    }

    [Fact]
    public async Task Guests_are_ignored()
    {
        Assert.Empty(await RunAsync([Member(type: UserType.Guest)], [Activity(success: Now.AddDays(-200))]));
    }

    [Fact]
    public async Task Activity_matches_users_whatever_the_case()
    {
        Assert.Empty(await RunAsync([Member()], [Activity(success: Now.AddDays(-1), id: UserId.ToUpperInvariant())]));
    }

    [Fact]
    public async Task Without_inactive_users_roles_are_not_read()
    {
        var reader = new FakeDirectoryReader(apps: [])
        {
            Users = [Member()],
            UserSignInActivity = [Activity(success: Now.AddDays(-1))],
            RoleAssignmentsFailure = new DirectoryReadException("denied", 403),
        };

        Assert.Empty(await RunAsync(reader));
    }

    [Fact]
    public async Task Fails_when_sign_in_activity_cannot_be_read()
    {
        var reader = new FakeDirectoryReader(apps: [])
        {
            Users = [Member()],
            UserSignInActivityFailure = new DirectoryReadException("licence", 403),
        };

        await Assert.ThrowsAsync<DirectoryReadException>(() => RunAsync(reader));
    }

    [Fact]
    public async Task Fails_when_role_assignments_cannot_be_read()
    {
        var reader = new FakeDirectoryReader(apps: [])
        {
            Users = [Member()],
            RoleAssignmentsFailure = new DirectoryReadException("denied", 403),
        };

        await Assert.ThrowsAsync<DirectoryReadException>(() => RunAsync(reader));
    }
}
