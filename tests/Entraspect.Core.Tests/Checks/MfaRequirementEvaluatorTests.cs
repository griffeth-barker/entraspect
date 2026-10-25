using Entraspect.Core.Checks;

namespace Entraspect.Core.Tests.Checks;

public sealed class MfaRequirementEvaluatorTests
{
    private const string UserId = "aaaaaaaa-0000-0000-0000-000000000001";
    private const string OtherUserId = "aaaaaaaa-0000-0000-0000-000000000002";
    private const string GroupId = "99999999-0000-0000-0000-000000000001";
    private const string ParentGroupId = "99999999-0000-0000-0000-000000000002";
    private const string GlobalAdmin = "62e90394-69f5-4237-9190-012177145e10";

    private static readonly DirectoryUser Member = User(UserType.Member);
    private static readonly DirectoryUser Guest = User(UserType.Guest);

    private static DirectoryUser User(UserType type) =>
        new()
        {
            Id = UserId,
            UserPrincipalName = "user@contoso.com",
            DisplayName = "User",
            UserType = type,
            AccountEnabled = true,
        };

    /// <summary>An enabled policy that requires MFA for every user and every app, unless overridden.</summary>
    private static ConditionalAccessPolicy Policy(
        string id = "p1",
        ConditionalAccessPolicyState state = ConditionalAccessPolicyState.Enabled,
        string[]? includeUsers = null,
        string[]? excludeUsers = null,
        string[]? includeGroups = null,
        string[]? excludeGroups = null,
        string[]? includeRoles = null,
        string[]? excludeRoles = null,
        GuestOrExternalUserTypes includeGuests = GuestOrExternalUserTypes.None,
        bool includeGuestsLimited = false,
        GuestOrExternalUserTypes excludeGuests = GuestOrExternalUserTypes.None,
        bool excludeGuestsLimited = false,
        string[]? includeApps = null,
        string[]? excludeApps = null,
        bool excludeAppFilter = false,
        string[]? userActions = null,
        string[]? authContexts = null,
        string[]? controls = null,
        GrantOperator grantOperator = GrantOperator.Or,
        bool authenticationStrength = false,
        bool narrowing = false,
        bool servicePrincipals = false) =>
        new()
        {
            Id = id,
            DisplayName = $"Policy {id}",
            State = state,
            IncludeUsers = includeUsers ?? ["All"],
            ExcludeUsers = excludeUsers ?? [],
            IncludeGroups = includeGroups ?? [],
            ExcludeGroups = excludeGroups ?? [],
            IncludeRoles = includeRoles ?? [],
            ExcludeRoles = excludeRoles ?? [],
            IncludeGuestTypes = includeGuests,
            IncludeGuestsLimitedToTenants = includeGuestsLimited,
            ExcludeGuestTypes = excludeGuests,
            ExcludeGuestsLimitedToTenants = excludeGuestsLimited,
            IncludeApplications = includeApps ?? ["All"],
            ExcludeApplications = excludeApps ?? [],
            ExcludeApplicationsByFilter = excludeAppFilter,
            IncludeUserActions = userActions ?? [],
            IncludeAuthenticationContexts = authContexts ?? [],
            GrantBuiltInControls = controls ?? ["mfa"],
            GrantOperator = grantOperator,
            HasAuthenticationStrength = authenticationStrength,
            HasNarrowingConditions = narrowing,
            TargetsServicePrincipals = servicePrincipals,
        };

    private static MfaRequirement Evaluate(
        ConditionalAccessPolicy policy,
        DirectoryUser? user = null,
        string[]? groups = null,
        string[]? roles = null) =>
        MfaRequirementEvaluator.Evaluate(
            [policy],
            user ?? Member,
            new HashSet<string>(groups ?? [], StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(roles ?? [], StringComparer.OrdinalIgnoreCase));

    private static void AssertFull(MfaRequirement result)
    {
        Assert.Single(result.FullyRequiredBy);
        Assert.Empty(result.SometimesRequiredBy);
        Assert.Empty(result.ReportOnlyRequiredBy);
    }

    private static void AssertSometimes(MfaRequirement result)
    {
        Assert.Empty(result.FullyRequiredBy);
        Assert.Single(result.SometimesRequiredBy);
        Assert.Empty(result.ReportOnlyRequiredBy);
    }

    private static void AssertNone(MfaRequirement result)
    {
        Assert.Empty(result.FullyRequiredBy);
        Assert.Empty(result.SometimesRequiredBy);
        Assert.Empty(result.ReportOnlyRequiredBy);
    }

    [Fact]
    public void All_users_all_apps_mfa_fully_requires_mfa()
    {
        AssertFull(Evaluate(Policy()));
    }

    [Fact]
    public void Includes_by_user_id()
    {
        AssertFull(Evaluate(Policy(includeUsers: [UserId.ToUpperInvariant()])));
        AssertNone(Evaluate(Policy(includeUsers: [OtherUserId])));
    }

    [Fact]
    public void Includes_by_group()
    {
        AssertFull(Evaluate(Policy(includeUsers: [], includeGroups: [GroupId]), groups: [GroupId]));
        AssertNone(Evaluate(Policy(includeUsers: [], includeGroups: [GroupId])));
    }

    [Fact]
    public void Includes_by_a_parent_group_the_user_is_a_nested_member_of()
    {
        // The group set is transitive: the user is in GroupId, which is a member of ParentGroupId.
        AssertFull(Evaluate(Policy(includeUsers: [], includeGroups: [ParentGroupId]), groups: [GroupId, ParentGroupId]));
    }

    [Fact]
    public void Includes_by_role()
    {
        AssertFull(Evaluate(Policy(includeUsers: [], includeRoles: [GlobalAdmin]), roles: [GlobalAdmin]));
        AssertNone(Evaluate(Policy(includeUsers: [], includeRoles: [GlobalAdmin])));
    }

    [Fact]
    public void Excludes_by_user_group_and_role()
    {
        AssertNone(Evaluate(Policy(excludeUsers: [UserId])));
        AssertNone(Evaluate(Policy(excludeGroups: [GroupId]), groups: [GroupId]));
        AssertNone(Evaluate(Policy(excludeGroups: [ParentGroupId]), groups: [GroupId, ParentGroupId]));
        AssertNone(Evaluate(Policy(excludeRoles: [GlobalAdmin]), roles: [GlobalAdmin]));
        AssertFull(Evaluate(Policy(excludeUsers: [OtherUserId], excludeGroups: [GroupId], excludeRoles: [GlobalAdmin])));
    }

    [Fact]
    public void Exclusion_wins_over_inclusion()
    {
        AssertNone(Evaluate(Policy(includeUsers: [UserId], excludeUsers: [UserId])));
        AssertNone(Evaluate(
            Policy(includeUsers: [], includeRoles: [GlobalAdmin], excludeGroups: [GroupId]),
            groups: [GroupId],
            roles: [GlobalAdmin]));
    }

    [Fact]
    public void All_guest_types_include_a_guest_but_not_a_member()
    {
        var policy = Policy(
            includeUsers: [],
            includeGuests: GuestOrExternalUserTypes.InternalGuest | GuestOrExternalUserTypes.B2bCollaborationGuest |
                GuestOrExternalUserTypes.OtherExternalUser);

        AssertFull(Evaluate(policy, Guest));
        AssertNone(Evaluate(policy, Member));
    }

    [Fact]
    public void One_of_the_two_guest_kinds_includes_a_guest_only_in_some_cases()
    {
        AssertSometimes(Evaluate(
            Policy(includeUsers: [], includeGuests: GuestOrExternalUserTypes.B2bCollaborationGuest),
            Guest));
    }

    [Fact]
    public void Guest_types_limited_to_specific_tenants_include_only_in_some_cases()
    {
        AssertSometimes(Evaluate(
            Policy(
                includeUsers: [],
                includeGuests: GuestOrExternalUserTypes.InternalGuest | GuestOrExternalUserTypes.B2bCollaborationGuest,
                includeGuestsLimited: true),
            Guest));
    }

    [Fact]
    public void Excluded_guest_types_exclude_a_guest_fully_or_partly()
    {
        const GuestOrExternalUserTypes both =
            GuestOrExternalUserTypes.InternalGuest | GuestOrExternalUserTypes.B2bCollaborationGuest;

        AssertNone(Evaluate(Policy(excludeGuests: both), Guest));
        AssertFull(Evaluate(Policy(excludeGuests: both), Member));
        AssertSometimes(Evaluate(Policy(excludeGuests: GuestOrExternalUserTypes.InternalGuest), Guest));
        AssertSometimes(Evaluate(Policy(excludeGuests: both, excludeGuestsLimited: true), Guest));
    }

    [Fact]
    public void Legacy_guests_or_external_users_value_includes_and_excludes_guests()
    {
        AssertFull(Evaluate(Policy(includeUsers: ["GuestsOrExternalUsers"]), Guest));
        AssertNone(Evaluate(Policy(includeUsers: ["GuestsOrExternalUsers"]), Member));
        AssertNone(Evaluate(Policy(excludeUsers: ["GuestsOrExternalUsers"]), Guest));
    }

    [Fact]
    public void And_grant_with_another_control_still_fully_requires_mfa()
    {
        AssertFull(Evaluate(Policy(controls: ["mfa", "compliantDevice"], grantOperator: GrantOperator.And)));
    }

    [Fact]
    public void Or_grant_with_another_control_requires_mfa_only_in_some_cases()
    {
        AssertSometimes(Evaluate(Policy(controls: ["mfa", "compliantDevice"], grantOperator: GrantOperator.Or)));
    }

    [Fact]
    public void Authentication_strength_counts_as_mfa()
    {
        AssertFull(Evaluate(Policy(controls: [], authenticationStrength: true)));
        AssertSometimes(Evaluate(Policy(controls: ["domainJoinedDevice"], authenticationStrength: true)));
        AssertFull(Evaluate(
            Policy(controls: ["domainJoinedDevice"], authenticationStrength: true, grantOperator: GrantOperator.And)));
    }

    [Fact]
    public void Policies_that_dont_require_mfa_are_ignored()
    {
        AssertNone(Evaluate(Policy(controls: ["compliantDevice"])));
        AssertNone(Evaluate(Policy(controls: ["block"])));
        AssertNone(Evaluate(Policy(controls: [])));
    }

    [Fact]
    public void Narrowing_conditions_require_mfa_only_in_some_cases()
    {
        AssertSometimes(Evaluate(Policy(narrowing: true)));
    }

    [Fact]
    public void Apps_that_are_not_all_or_excluded_require_mfa_only_in_some_cases()
    {
        AssertSometimes(Evaluate(Policy(includeApps: ["Office365"])));
        AssertSometimes(Evaluate(Policy(excludeApps: ["00000002-0000-0ff1-ce00-000000000000"])));
        AssertSometimes(Evaluate(Policy(excludeAppFilter: true)));
        AssertSometimes(Evaluate(Policy(includeApps: [], userActions: ["urn:user:registersecurityinfo"])));
        AssertSometimes(Evaluate(Policy(includeApps: [], authContexts: ["c1"])));
    }

    [Fact]
    public void Report_only_policies_are_listed_separately()
    {
        var result = Evaluate(Policy(state: ConditionalAccessPolicyState.ReportOnly));

        Assert.Empty(result.FullyRequiredBy);
        Assert.Empty(result.SometimesRequiredBy);
        Assert.Equal("p1", Assert.Single(result.ReportOnlyRequiredBy).Id);
        AssertNone(Evaluate(Policy(state: ConditionalAccessPolicyState.ReportOnly, excludeUsers: [UserId])));
    }

    [Fact]
    public void Disabled_policies_are_ignored()
    {
        AssertNone(Evaluate(Policy(state: ConditionalAccessPolicyState.Disabled)));
    }

    [Fact]
    public void Service_principal_policies_are_ignored()
    {
        AssertNone(Evaluate(Policy(servicePrincipals: true)));
    }

    [Fact]
    public void Each_policy_is_sorted_into_its_list()
    {
        var result = MfaRequirementEvaluator.Evaluate(
            [
                Policy("full"),
                Policy("partial", narrowing: true),
                Policy("report", state: ConditionalAccessPolicyState.ReportOnly),
                Policy("excluded", excludeUsers: [UserId]),
            ],
            Member,
            new HashSet<string>(),
            new HashSet<string>());

        Assert.Equal("full", Assert.Single(result.FullyRequiredBy).Id);
        Assert.Equal("partial", Assert.Single(result.SometimesRequiredBy).Id);
        Assert.Equal("report", Assert.Single(result.ReportOnlyRequiredBy).Id);
    }

    [Fact]
    public void Is_relevant_only_for_active_user_policies_that_require_mfa()
    {
        Assert.True(MfaRequirementEvaluator.IsRelevant(Policy()));
        Assert.True(MfaRequirementEvaluator.IsRelevant(Policy(state: ConditionalAccessPolicyState.ReportOnly)));
        Assert.False(MfaRequirementEvaluator.IsRelevant(Policy(state: ConditionalAccessPolicyState.Disabled)));
        Assert.False(MfaRequirementEvaluator.IsRelevant(Policy(servicePrincipals: true)));
        Assert.False(MfaRequirementEvaluator.IsRelevant(Policy(controls: ["compliantDevice"])));
    }
}
