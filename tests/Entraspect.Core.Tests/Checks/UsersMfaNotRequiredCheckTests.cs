using System.Collections.Generic;
using System.Linq;
using Entraspect.Core.Checks;
using Microsoft.Extensions.Time.Testing;

namespace Entraspect.Core.Tests.Checks;

public sealed class UsersMfaNotRequiredCheckTests
{
    private const string AdminId = "aaaaaaaa-0000-0000-0000-000000000001";
    private const string MemberId = "aaaaaaaa-0000-0000-0000-000000000002";
    private const string GuestId = "aaaaaaaa-0000-0000-0000-000000000003";
    private const string ExcludedGroupId = "99999999-0000-0000-0000-000000000001";
    private const string RoleGroupId = "99999999-0000-0000-0000-000000000002";
    private const string ServicePrincipalId = "bbbbbbbb-0000-0000-0000-000000000001";
    private const string GlobalAdmin = "62e90394-69f5-4237-9190-012177145e10";
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Now);

    private static DirectoryUser User(string id, UserType type = UserType.Member, bool enabled = true) =>
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
        User(AdminId),
        User(MemberId),
        User(GuestId, UserType.Guest),
    ];

    private static readonly DirectoryRoleAssignment[] AdminRole =
    [
        new() { RoleDefinitionId = GlobalAdmin, PrincipalId = AdminId },
    ];

    /// <summary>An enabled policy that requires MFA for every user and every app, unless overridden.</summary>
    private static ConditionalAccessPolicy Policy(
        string name = "Require MFA",
        ConditionalAccessPolicyState state = ConditionalAccessPolicyState.Enabled,
        string[]? includeUsers = null,
        string[]? excludeUsers = null,
        string[]? excludeGroups = null,
        string[]? includeRoles = null,
        bool narrowing = false,
        string[]? controls = null) =>
        new()
        {
            Id = name,
            DisplayName = name,
            State = state,
            IncludeUsers = includeUsers ?? ["All"],
            ExcludeUsers = excludeUsers ?? [],
            ExcludeGroups = excludeGroups ?? [],
            IncludeRoles = includeRoles ?? [],
            IncludeApplications = ["All"],
            GrantBuiltInControls = controls ?? ["mfa"],
            HasNarrowingConditions = narrowing,
        };

    private static FakeDirectoryReader Reader(
        ConditionalAccessPolicy[]? policies = null,
        DirectoryUser[]? users = null,
        DirectoryRoleAssignment[]? roles = null,
        Dictionary<string, IReadOnlyList<string>>? groups = null,
        Dictionary<string, PerUserMfaState>? perUserMfa = null,
        Dictionary<string, SingleFactorSignIns>? signIns = null,
        bool securityDefaults = false) =>
        new([], policies: policies ?? [])
        {
            Users = users ?? Users,
            RoleAssignments = roles ?? AdminRole,
            GroupMembers = groups ?? new Dictionary<string, IReadOnlyList<string>>(),
            PerUserMfaStates = perUserMfa ?? new Dictionary<string, PerUserMfaState>(),
            SignIns = signIns ?? new Dictionary<string, SingleFactorSignIns>(),
            SecurityDefaultsEnabled = securityDefaults,
        };

    private Task<IReadOnlyList<Finding>> RunAsync(FakeDirectoryReader reader, CheckOptions? options = null) =>
        new UsersMfaNotRequiredCheck(_time).RunAsync(reader, options ?? new CheckOptions());

    private static Finding For(IReadOnlyList<Finding> findings, string userId) =>
        Assert.Single(findings, f => f.ObjectId == userId);

    [Fact]
    public async Task Security_defaults_give_one_tenant_finding_and_no_user_findings()
    {
        var reader = Reader(securityDefaults: true);

        var finding = Assert.Single(await RunAsync(reader));

        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Equal("Tenant", finding.ObjectType);
        Assert.Equal(UsersMfaNotRequiredCheck.SecurityDefaultsMessage, finding.Message);
        Assert.Empty(reader.PerUserMfaReads);
        Assert.Empty(reader.SignInReads);
    }

    [Fact]
    public async Task With_no_policy_admins_are_critical_members_warning_and_guests_info()
    {
        var findings = await RunAsync(Reader());

        Assert.Equal(3, findings.Count);
        Assert.Equal(Severity.Critical, For(findings, AdminId).Severity);
        Assert.Equal(Severity.Warning, For(findings, MemberId).Severity);
        Assert.Equal(Severity.Info, For(findings, GuestId).Severity);
        Assert.All(findings, f => Assert.Equal(UsersMfaNotRequiredCheck.NotRequiredMessage, f.Message));
    }

    [Fact]
    public async Task Findings_name_the_user_with_the_upn_in_its_own_field()
    {
        var finding = For(await RunAsync(Reader()), MemberId);

        Assert.Equal(("users-mfa-not-required", "User"), (finding.CheckId, finding.ObjectType));
        Assert.Equal("User 2", finding.ObjectDisplayName);
        Assert.Equal($"{MemberId}@contoso.com", finding.UserPrincipalName);
        Assert.Null(finding.SubObjectType);
        Assert.Null(finding.SubObjectDisplayName);
        Assert.Null(finding.DueDate);
    }

    [Fact]
    public async Task Disabled_users_are_skipped()
    {
        var reader = Reader(users: [User(MemberId, enabled: false)]);

        Assert.Empty(await RunAsync(reader));
        Assert.Empty(reader.PerUserMfaReads);
    }

    [Fact]
    public async Task A_user_one_full_policy_covers_has_no_finding()
    {
        var reader = Reader(policies: [Policy(excludeUsers: [MemberId])]);

        var finding = Assert.Single(await RunAsync(reader));

        Assert.Equal(MemberId, finding.ObjectId);
        Assert.Equal(new[] { MemberId }, reader.PerUserMfaReads);
    }

    [Fact]
    public async Task A_member_excluded_through_a_group_is_a_warning()
    {
        var reader = Reader(
            policies: [Policy(excludeGroups: [ExcludedGroupId])],
            groups: new() { [ExcludedGroupId] = [MemberId] });

        var finding = Assert.Single(await RunAsync(reader));

        Assert.Equal((MemberId, Severity.Warning), (finding.ObjectId, finding.Severity));
        Assert.Contains(ExcludedGroupId, reader.GroupMemberReads);
    }

    [Fact]
    public async Task A_role_held_through_a_role_assignable_group_makes_the_user_critical()
    {
        var reader = Reader(
            policies: [Policy(excludeUsers: [MemberId, GuestId, AdminId])],
            users: [User(MemberId)],
            roles: [new() { RoleDefinitionId = GlobalAdmin, PrincipalId = RoleGroupId }],
            groups: new() { [RoleGroupId] = [MemberId] });

        var finding = Assert.Single(await RunAsync(reader));

        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal(new[] { RoleGroupId }, reader.GroupMemberReads);
    }

    [Fact]
    public async Task A_policy_that_includes_a_role_covers_users_who_hold_it_through_a_group()
    {
        var reader = Reader(
            policies: [Policy(includeUsers: [], includeRoles: [GlobalAdmin])],
            users: [User(MemberId)],
            roles: [new() { RoleDefinitionId = GlobalAdmin, PrincipalId = RoleGroupId }],
            groups: new() { [RoleGroupId] = [MemberId] });

        Assert.Empty(await RunAsync(reader));
    }

    [Fact]
    public async Task Role_principals_that_are_users_are_not_read_as_groups()
    {
        var reader = Reader(
            roles: [AdminRole[0], new() { RoleDefinitionId = GlobalAdmin, PrincipalId = ServicePrincipalId }]);

        await RunAsync(reader);

        Assert.Equal(new[] { ServicePrincipalId }, reader.GroupMemberReads);
    }

    [Fact]
    public async Task Mfa_required_only_in_some_cases_is_one_level_lower_and_names_the_policies()
    {
        var reader = Reader(policies: [Policy("Outside trusted locations", narrowing: true), Policy("Office", narrowing: true)]);

        var findings = await RunAsync(reader);

        Assert.Equal(Severity.Warning, For(findings, AdminId).Severity);
        Assert.Equal(Severity.Info, For(findings, MemberId).Severity);
        Assert.Equal(Severity.Info, For(findings, GuestId).Severity);
        Assert.Equal(
            "MFA is only required in some cases, by: Outside trusted locations, Office.",
            For(findings, MemberId).Message);
    }

    [Fact]
    public async Task Report_only_policies_that_would_require_mfa_are_named()
    {
        var reader = Reader(policies: [Policy("Pilot MFA", state: ConditionalAccessPolicyState.ReportOnly)]);

        var finding = For(await RunAsync(reader), MemberId);

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal(
            UsersMfaNotRequiredCheck.NotRequiredMessage + " Report-only policies that would require MFA: Pilot MFA.",
            finding.Message);
    }

    [Fact]
    public async Task Disabled_and_non_mfa_policies_dont_cover_anyone()
    {
        var reader = Reader(
            policies:
            [
                Policy("Off", state: ConditionalAccessPolicyState.Disabled),
                Policy("Compliant device", controls: ["compliantDevice"]),
            ]);

        Assert.Equal(3, (await RunAsync(reader)).Count);
    }

    [Theory]
    [InlineData(PerUserMfaState.Enabled)]
    [InlineData(PerUserMfaState.Enforced)]
    public async Task Legacy_per_user_mfa_is_info_with_its_own_message(PerUserMfaState state)
    {
        var reader = Reader(perUserMfa: new() { [AdminId] = state });

        var finding = For(await RunAsync(reader), AdminId);

        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Equal(UsersMfaNotRequiredCheck.PerUserMfaMessage, finding.Message);
    }

    [Fact]
    public async Task Unknown_per_user_mfa_state_carries_on_as_disabled_with_a_note()
    {
        var reader = Reader(perUserMfa: new() { [MemberId] = PerUserMfaState.Unknown });

        var finding = For(await RunAsync(reader), MemberId);

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal(
            UsersMfaNotRequiredCheck.NotRequiredMessage + " Per-user MFA state couldn't be read.",
            finding.Message);
    }

    [Fact]
    public async Task Single_factor_sign_ins_are_evidence_without_changing_the_severity()
    {
        var last = Now.AddDays(-2);
        var reader = Reader(signIns: new() { [MemberId] = new(4, last), [GuestId] = new(1, last) });

        var findings = await RunAsync(reader, new CheckOptions { SignInLookbackDays = 14 });

        var member = For(findings, MemberId);
        Assert.Equal(Severity.Warning, member.Severity);
        Assert.Equal(
            UsersMfaNotRequiredCheck.NotRequiredMessage +
            " 4 successful single-factor sign-ins in the last 14 days.",
            member.Message);
        Assert.Equal(last, member.DueDate);
        Assert.EndsWith(" 1 successful single-factor sign-in in the last 14 days.", For(findings, GuestId).Message);
        Assert.Null(For(findings, AdminId).DueDate);
        Assert.Equal(new[] { Now.AddDays(-14) }, reader.SignInReads);
    }

    [Fact]
    public async Task One_day_lookback_says_day()
    {
        var reader = Reader(signIns: new() { [MemberId] = new(2, Now.AddHours(-1)) });

        var finding = For(await RunAsync(reader, new CheckOptions { SignInLookbackDays = 1 }), MemberId);

        Assert.EndsWith(" 2 successful single-factor sign-ins in the last day.", finding.Message);
    }

    [Fact]
    public async Task Sign_ins_are_not_read_when_no_finding_needs_them()
    {
        var covered = Reader(policies: [Policy()]);
        var legacyOnly = Reader(
            users: [User(MemberId)],
            perUserMfa: new() { [MemberId] = PerUserMfaState.Enforced });

        Assert.Empty(await RunAsync(covered));
        Assert.Single(await RunAsync(legacyOnly));

        Assert.Empty(covered.SignInReads);
        Assert.Empty(covered.PerUserMfaReads);
        Assert.Empty(legacyOnly.SignInReads);
    }

    [Fact]
    public async Task Sign_ins_are_read_once()
    {
        var reader = Reader();

        await RunAsync(reader);

        Assert.Single(reader.SignInReads);
    }

    [Fact]
    public async Task Reports_progress_reading_per_user_mfa_state()
    {
        var reports = new List<string>();

        await new UsersMfaNotRequiredCheck(_time).RunAsync(
            Reader(), new CheckOptions(), new SyncProgress<string>(reports.Add), CancellationToken.None);

        Assert.Equal(
            new[]
            {
                "reading per-user MFA state 1 of 3", "reading per-user MFA state 2 of 3",
                "reading per-user MFA state 3 of 3",
            },
            reports);
    }

    [Fact]
    public async Task Groups_of_policies_that_dont_require_mfa_are_not_read()
    {
        var reader = Reader(
            policies: [Policy("Compliant device", excludeGroups: [ExcludedGroupId], controls: ["compliantDevice"])],
            roles: []);

        await RunAsync(reader);

        Assert.Empty(reader.GroupMemberReads);
    }
}
