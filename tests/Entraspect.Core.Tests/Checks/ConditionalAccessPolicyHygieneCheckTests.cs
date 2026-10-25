using System.Collections.Generic;
using System.Linq;
using Entraspect.Core.Checks;
using Microsoft.Extensions.Time.Testing;
using static Entraspect.Core.Tests.Checks.GroupTestData;

namespace Entraspect.Core.Tests.Checks;

public sealed class ConditionalAccessPolicyHygieneCheckTests
{
    private const string UserA = "aaaaaaaa-0000-0000-0000-000000000001";
    private const string UserB = "aaaaaaaa-0000-0000-0000-000000000002";
    private const string DeletedUser = "dddddddd-0000-0000-0000-000000000001";
    private const string GroupWithMembers = "99999999-0000-0000-0000-000000000001";
    private const string EmptyGroup = "99999999-0000-0000-0000-000000000002";
    private const string OtherEmptyGroup = "99999999-0000-0000-0000-000000000003";
    private const string DeletedGroup = "dddddddd-0000-0000-0000-000000000002";
    private const string GlobalAdminRole = "62e90394-69f5-4237-9190-012177145e10";

    private static readonly DirectoryGroup[] Groups =
    [
        Group(GroupWithMembers, hasMembers: true),
        Group(EmptyGroup, hasMembers: false),
        Group(OtherEmptyGroup, hasMembers: false),
    ];

    private readonly FakeTimeProvider _time = new(Now);

    /// <summary>An enabled policy that includes every user and every app, so no rule applies unless overridden.</summary>
    private static ConditionalAccessPolicy Policy(
        string id = "p1",
        ConditionalAccessPolicyState state = ConditionalAccessPolicyState.Enabled,
        DateTimeOffset? created = null,
        DateTimeOffset? modified = null,
        string[]? includeUsers = null,
        string[]? excludeUsers = null,
        string[]? includeGroups = null,
        string[]? excludeGroups = null,
        string[]? includeRoles = null,
        bool guests = false,
        string[]? includeApps = null,
        bool appFilter = false,
        string[]? userActions = null,
        string[]? authContexts = null,
        bool servicePrincipals = false) =>
        new()
        {
            Id = id,
            DisplayName = $"Policy {id}",
            State = state,
            CreatedDateTime = created ?? Now.AddYears(-1),
            ModifiedDateTime = modified,
            IncludeUsers = includeUsers ?? ["All"],
            ExcludeUsers = excludeUsers ?? [],
            IncludeGroups = includeGroups ?? [],
            ExcludeGroups = excludeGroups ?? [],
            IncludeRoles = includeRoles ?? [],
            IncludeGuestsOrExternalUsers = guests,
            IncludeApplications = includeApps ?? ["All"],
            IncludeApplicationsByFilter = appFilter,
            IncludeUserActions = userActions ?? [],
            IncludeAuthenticationContexts = authContexts ?? [],
            TargetsServicePrincipals = servicePrincipals,
        };

    private static string[] List(string commaSeparated) =>
        commaSeparated.Length == 0 ? [] : commaSeparated.Split(',');

    private static FakeDirectoryReader Reader(params ConditionalAccessPolicy[] policies) =>
        new([], Groups, policies: policies, existingUserIds: [UserA, UserB]);

    private Task<IReadOnlyList<Finding>> RunAsync(params ConditionalAccessPolicy[] policies) =>
        RunAsync(new CheckOptions(), policies);

    private Task<IReadOnlyList<Finding>> RunAsync(CheckOptions options, params ConditionalAccessPolicy[] policies) =>
        new ConditionalAccessPolicyHygieneCheck(_time).RunAsync(Reader(policies), options);

    [Fact]
    public async Task Enabled_policy_for_all_users_and_apps_has_no_finding()
    {
        Assert.Empty(await RunAsync(Policy()));
    }

    [Fact]
    public async Task Disabled_policy_is_info_with_its_modified_date()
    {
        var modified = Now.AddDays(-40);

        var f = Assert.Single(await RunAsync(Policy(state: ConditionalAccessPolicyState.Disabled, modified: modified)));

        Assert.Equal("ca-policy-hygiene", f.CheckId);
        Assert.Equal(Severity.Info, f.Severity);
        Assert.Equal("ConditionalAccessPolicy", f.ObjectType);
        Assert.Equal("p1", f.ObjectId);
        Assert.Equal("Policy p1", f.ObjectDisplayName);
        Assert.Equal("Policy is disabled.", f.Message);
        Assert.Equal("Disabled", f.SubObjectType);
        Assert.Null(f.SubObjectDisplayName);
        Assert.Equal(modified, f.DueDate);
    }

    [Fact]
    public async Task Disabled_policy_gets_no_other_findings_and_its_users_are_not_looked_up()
    {
        var reader = Reader(Policy(
            state: ConditionalAccessPolicyState.Disabled,
            includeUsers: [DeletedUser],
            includeApps: ["None"],
            excludeGroups: [DeletedGroup]));

        var findings = await new ConditionalAccessPolicyHygieneCheck(_time).RunAsync(reader, new CheckOptions());

        Assert.Equal("Disabled", Assert.Single(findings).SubObjectType);
        Assert.Empty(reader.UserLookups);
    }

    [Theory]
    [InlineData(31, true)]
    [InlineData(30, true)]
    [InlineData(29, false)]
    public async Task Report_only_policy_is_a_warning_from_the_report_only_warning_days(int daysAgo, bool expected)
    {
        var findings = await RunAsync(Policy(
            state: ConditionalAccessPolicyState.ReportOnly,
            modified: Now.AddDays(-daysAgo)));

        if (expected)
        {
            var f = Assert.Single(findings);
            Assert.Equal(Severity.Warning, f.Severity);
            Assert.Equal("Report-only", f.SubObjectType);
            Assert.Equal($"Policy has been in report-only mode for {daysAgo} days and is not enforced.", f.Message);
        }
        else
        {
            Assert.Empty(findings);
        }
    }

    [Fact]
    public async Task Report_only_days_are_utc_calendar_days()
    {
        // 23:59 UTC the day before is one calendar day ago, though less than a day has passed.
        var options = new CheckOptions { ReportOnlyWarningDays = 1 };
        var modified = new DateTimeOffset(Now.UtcDateTime.Date.AddMinutes(-1), TimeSpan.Zero);

        var f = Assert.Single(await RunAsync(options, Policy(state: ConditionalAccessPolicyState.ReportOnly, modified: modified)));

        Assert.Equal("Policy has been in report-only mode for 1 day and is not enforced.", f.Message);
    }

    [Fact]
    public async Task Zero_report_only_warning_days_flags_a_policy_changed_today()
    {
        var options = new CheckOptions { ReportOnlyWarningDays = 0 };

        var f = Assert.Single(await RunAsync(options, Policy(state: ConditionalAccessPolicyState.ReportOnly, modified: Now)));

        Assert.Equal("Report-only", f.SubObjectType);
    }

    [Fact]
    public async Task Report_only_age_uses_the_created_date_when_never_modified()
    {
        var created = Now.AddDays(-45);

        var f = Assert.Single(await RunAsync(Policy(state: ConditionalAccessPolicyState.ReportOnly, created: created)));

        Assert.Contains("for 45 days", f.Message);
        Assert.Equal(created, f.DueDate);
    }

    [Fact]
    public async Task Enabled_policy_is_never_report_only()
    {
        Assert.Empty(await RunAsync(Policy(modified: Now.AddYears(-2))));
    }

    // Comma-separated, so "" is an empty list and "none," is "none" and an empty string.
    [Theory]
    [InlineData("")]
    [InlineData("None")]
    [InlineData("none,")]
    public async Task Policy_with_no_users_is_a_warning(string includeUsers)
    {
        var f = Assert.Single(await RunAsync(Policy(includeUsers: List(includeUsers))));

        Assert.Equal(Severity.Warning, f.Severity);
        Assert.Equal("No users", f.SubObjectType);
        Assert.Equal("Policy doesn't include any users, so it never applies.", f.Message);
    }

    [Fact]
    public async Task Users_roles_guests_groups_or_service_principals_count_as_targets()
    {
        Assert.Empty(await RunAsync(
            Policy(id: "users", includeUsers: [UserA]),
            Policy(id: "guest-value", includeUsers: ["GuestsOrExternalUsers"]),
            Policy(id: "roles", includeUsers: ["None"], includeRoles: [GlobalAdminRole]),
            Policy(id: "guests", includeUsers: [], guests: true),
            Policy(id: "groups", includeUsers: [], includeGroups: [GroupWithMembers])));
    }

    [Fact]
    public async Task Service_principal_policy_is_not_flagged_as_targeting_no_users()
    {
        Assert.Empty(await RunAsync(Policy(includeUsers: ["None"], servicePrincipals: true)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("None")]
    public async Task Policy_with_no_apps_is_a_warning(string includeApps)
    {
        var f = Assert.Single(await RunAsync(Policy(includeApps: List(includeApps))));

        Assert.Equal(Severity.Warning, f.Severity);
        Assert.Equal("No apps", f.SubObjectType);
        Assert.Equal("Policy doesn't include any apps, so it never applies.", f.Message);
    }

    [Fact]
    public async Task Special_app_values_user_actions_auth_contexts_and_filters_count_as_apps()
    {
        Assert.Empty(await RunAsync(
            Policy(id: "office", includeApps: ["Office365"]),
            Policy(id: "portals", includeApps: ["MicrosoftAdminPortals"]),
            Policy(id: "app", includeApps: ["00000003-0000-0ff1-ce00-000000000000"]),
            Policy(id: "action", includeApps: [], userActions: ["urn:user:registersecurityinfo"]),
            Policy(id: "context", includeApps: ["None"], authContexts: ["c1"]),
            Policy(id: "filter", includeApps: ["None"], appFilter: true)));
    }

    [Fact]
    public async Task Policy_including_only_empty_groups_is_a_warning()
    {
        var f = Assert.Single(await RunAsync(Policy(includeUsers: ["None"], includeGroups: [EmptyGroup, OtherEmptyGroup])));

        Assert.Equal(Severity.Warning, f.Severity);
        Assert.Equal("Empty groups", f.SubObjectType);
        Assert.Equal("Every group this policy includes is empty, so it applies to no one.", f.Message);
    }

    [Fact]
    public async Task One_group_with_members_or_any_direct_target_means_groups_are_not_only_empty()
    {
        Assert.Empty(await RunAsync(
            Policy(id: "member", includeUsers: [], includeGroups: [EmptyGroup, GroupWithMembers]),
            Policy(id: "user", includeUsers: [UserA], includeGroups: [EmptyGroup]),
            Policy(id: "role", includeUsers: [], includeRoles: [GlobalAdminRole], includeGroups: [EmptyGroup]),
            Policy(id: "guest", includeUsers: [], guests: true, includeGroups: [EmptyGroup])));
    }

    [Fact]
    public async Task Excluding_an_empty_group_is_not_a_finding()
    {
        Assert.Empty(await RunAsync(Policy(excludeGroups: [EmptyGroup])));
    }

    [Fact]
    public async Task Included_deleted_user_and_group_are_warnings_with_the_id()
    {
        var findings = await RunAsync(Policy(includeUsers: [UserA, DeletedUser], includeGroups: [DeletedGroup]));

        Assert.Equal(2, findings.Count);
        Assert.All(findings, f =>
        {
            Assert.Equal(Severity.Warning, f.Severity);
            Assert.Equal("Deleted reference", f.SubObjectType);
        });
        Assert.Equal(new[] { DeletedUser, DeletedGroup }, findings.Select(f => f.SubObjectDisplayName));
        Assert.Equal($"Policy includes a user that has been deleted: {DeletedUser}.", findings[0].Message);
        Assert.Equal($"Policy includes a group that has been deleted: {DeletedGroup}.", findings[1].Message);
    }

    [Fact]
    public async Task Excluded_deleted_user_and_group_are_info_with_the_id()
    {
        var findings = await RunAsync(Policy(excludeUsers: [DeletedUser, UserB], excludeGroups: [GroupWithMembers, DeletedGroup]));

        Assert.Equal(2, findings.Count);
        Assert.All(findings, f => Assert.Equal(Severity.Info, f.Severity));
        Assert.Equal(new[] { DeletedUser, DeletedGroup }, findings.Select(f => f.SubObjectDisplayName));
        Assert.Equal(
            $"Policy excludes a user that has been deleted: {DeletedUser}; remove it from the policy.",
            findings[0].Message);
        Assert.Equal(
            $"Policy excludes a group that has been deleted: {DeletedGroup}; remove it from the policy.",
            findings[1].Message);
    }

    [Fact]
    public async Task Special_user_values_are_not_looked_up_or_reported_as_deleted()
    {
        var reader = Reader(
            Policy(id: "p1", includeUsers: ["All"], excludeUsers: ["GuestsOrExternalUsers", UserA]),
            Policy(id: "p2", includeUsers: ["None", "GuestsOrExternalUsers", UserA, DeletedUser], includeGroups: ["All"]));

        var findings = await new ConditionalAccessPolicyHygieneCheck(_time).RunAsync(reader, new CheckOptions());

        Assert.Equal(DeletedUser, Assert.Single(findings).SubObjectDisplayName);
        Assert.Equal(new[] { UserA, DeletedUser }, Assert.Single(reader.UserLookups));
    }

    [Fact]
    public async Task Policy_with_several_problems_gets_one_finding_per_rule()
    {
        var findings = await RunAsync(Policy(
            state: ConditionalAccessPolicyState.ReportOnly,
            modified: Now.AddDays(-60),
            includeUsers: ["None"],
            includeGroups: [EmptyGroup, DeletedGroup],
            includeApps: ["None"],
            excludeUsers: [DeletedUser]));

        Assert.Equal(
            new[] { "Report-only", "No apps", "Empty groups", "Deleted reference", "Deleted reference" },
            findings.Select(f => f.SubObjectType));
        Assert.Equal(
            new[] { Severity.Warning, Severity.Warning, Severity.Warning, Severity.Warning, Severity.Info },
            findings.Select(f => f.Severity));
        Assert.All(findings, f => Assert.Equal("p1", f.ObjectId));
    }

    [Fact]
    public async Task Only_deleted_included_groups_are_reported_as_deleted_not_empty()
    {
        var findings = await RunAsync(Policy(includeUsers: [], includeGroups: [DeletedGroup]));

        Assert.Equal("Deleted reference", Assert.Single(findings).SubObjectType);
    }

    [Fact]
    public async Task Each_user_is_looked_up_once_across_policies_and_progress_is_reported()
    {
        var reader = Reader(
            Policy(id: "p1", includeUsers: [UserA, UserB]),
            Policy(id: "p2", includeUsers: [UserA.ToUpperInvariant()], excludeUsers: [DeletedUser]));
        var progress = new List<string>();

        await new ConditionalAccessPolicyHygieneCheck(_time)
            .RunAsync(reader, new CheckOptions(), new SyncProgress<string>(progress.Add), default);

        Assert.Equal(new[] { UserA, UserB, DeletedUser }, Assert.Single(reader.UserLookups));
        Assert.Equal(
            new[] { "looking up user 1 of 3", "looking up user 2 of 3", "looking up user 3 of 3" },
            progress);
    }

    [Fact]
    public async Task No_policies_means_no_reads_of_users()
    {
        var reader = Reader();

        Assert.Empty(await new ConditionalAccessPolicyHygieneCheck(_time).RunAsync(reader, new CheckOptions()));
        Assert.Empty(reader.UserLookups);
    }

    [Theory]
    [InlineData("All", false)]
    [InlineData("None", false)]
    [InlineData("GuestsOrExternalUsers", false)]
    [InlineData("Office365", false)]
    [InlineData("MicrosoftAdminPortals", false)]
    [InlineData("", false)]
    [InlineData(UserA, true)]
    public void Special_values_are_not_object_ids(string value, bool expected)
    {
        Assert.Equal(expected, ConditionalAccessPolicyHygieneCheck.IsObjectId(value));
    }
}
