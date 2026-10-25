using System.Collections.Generic;
using System.Linq;

namespace Entraspect.Core.Checks;

/// <summary>The Conditional Access policies that require MFA for one user, by how far they require it.</summary>
public sealed record MfaRequirement(
    IReadOnlyList<ConditionalAccessPolicy> FullyRequiredBy,
    IReadOnlyList<ConditionalAccessPolicy> SometimesRequiredBy,
    IReadOnlyList<ConditionalAccessPolicy> ReportOnlyRequiredBy);

/// <summary>
/// Works out which Conditional Access policies require MFA for a user, without simulating sign-in conditions. A policy
/// fully requires MFA when it is enabled, has the user fully in scope, needs MFA or an authentication strength whatever
/// else the user satisfies, and applies to every app under every condition. An enabled policy that requires MFA for
/// the user only some of the time requires it "in some cases". Disabled policies and policies for workload identities
/// are ignored.
/// </summary>
public static class MfaRequirementEvaluator
{
    private const string MfaControl = "mfa";
    private const string All = "All";

    /// <summary>The legacy special value in a user list for every guest and external user.</summary>
    private const string GuestsOrExternalUsers = "GuestsOrExternalUsers";

    // A guest could be either kind; entraspect doesn't read where a guest's account lives. Members aren't matched to
    // any guest type.
    private const GuestOrExternalUserTypes PossibleGuestTypes =
        GuestOrExternalUserTypes.InternalGuest | GuestOrExternalUserTypes.B2bCollaborationGuest;

    private enum Scope
    {
        None,
        Partial,
        Full,
    }

    /// <param name="groupIds">The groups the user is a member of, directly or through nested groups.</param>
    /// <param name="roleIds">
    /// The role definition IDs the user holds through an active assignment, directly or through a role-assignable group.
    /// </param>
    public static MfaRequirement Evaluate(
        IEnumerable<ConditionalAccessPolicy> policies,
        DirectoryUser user,
        IReadOnlySet<string> groupIds,
        IReadOnlySet<string> roleIds)
    {
        var full = new List<ConditionalAccessPolicy>();
        var sometimes = new List<ConditionalAccessPolicy>();
        var reportOnly = new List<ConditionalAccessPolicy>();

        foreach (var policy in policies.Where(IsRelevant))
        {
            var scope = UserScope(policy, user, groupIds, roleIds);
            if (scope == Scope.None)
            {
                continue;
            }

            if (policy.State == ConditionalAccessPolicyState.ReportOnly)
            {
                reportOnly.Add(policy);
            }
            else if (scope == Scope.Full && AlwaysRequiresMfa(policy) && AppliesEverywhere(policy))
            {
                full.Add(policy);
            }
            else
            {
                sometimes.Add(policy);
            }
        }

        return new MfaRequirement(full, sometimes, reportOnly);
    }

    /// <summary>
    /// Whether the policy is one the evaluator looks at: enabled or report-only, aimed at users rather than workload
    /// identities, and requiring MFA. The groups and roles of other policies don't need to be read.
    /// </summary>
    public static bool IsRelevant(ConditionalAccessPolicy policy) =>
        policy.State != ConditionalAccessPolicyState.Disabled && !policy.TargetsServicePrincipals && RequiresMfa(policy);

    /// <summary>Whether the grant controls ask for MFA: the <c>mfa</c> control or any authentication strength.</summary>
    public static bool RequiresMfa(ConditionalAccessPolicy policy) =>
        policy.HasAuthenticationStrength || policy.GrantBuiltInControls.Any(IsMfa);

    /// <summary>
    /// Whether the user can't satisfy the policy without MFA: with <c>OR</c>, another control such as a compliant
    /// device would also do.
    /// </summary>
    private static bool AlwaysRequiresMfa(ConditionalAccessPolicy policy) =>
        RequiresMfa(policy) &&
        (policy.GrantOperator == GrantOperator.And || policy.GrantBuiltInControls.All(IsMfa));

    private static bool AppliesEverywhere(ConditionalAccessPolicy policy) =>
        policy.IncludeApplications.Any(IsAll) &&
        policy.ExcludeApplications.Count == 0 &&
        !policy.ExcludeApplicationsByFilter &&
        policy.IncludeUserActions.Count == 0 &&
        policy.IncludeAuthenticationContexts.Count == 0 &&
        !policy.HasNarrowingConditions;

    private static Scope UserScope(
        ConditionalAccessPolicy policy,
        DirectoryUser user,
        IReadOnlySet<string> groupIds,
        IReadOnlySet<string> roleIds)
    {
        var guestTypes = user.UserType == UserType.Guest ? PossibleGuestTypes : GuestOrExternalUserTypes.None;
        var isGuest = guestTypes != GuestOrExternalUserTypes.None;

        // Exclusion wins over inclusion.
        if (policy.ExcludeUsers.Any(id => Same(id, user.Id) || (isGuest && Same(id, GuestsOrExternalUsers))) ||
            policy.ExcludeGroups.Any(groupIds.Contains) ||
            policy.ExcludeRoles.Any(roleIds.Contains))
        {
            return Scope.None;
        }

        var cap = Scope.Full;
        var excludedTypes = policy.ExcludeGuestTypes & guestTypes;
        if (excludedTypes != GuestOrExternalUserTypes.None)
        {
            if (excludedTypes == guestTypes && !policy.ExcludeGuestsLimitedToTenants)
            {
                return Scope.None;
            }

            // The user may or may not be excluded, depending on which kind of guest they are or their home tenant.
            cap = Scope.Partial;
        }

        var included = Scope.None;
        if (policy.IncludeUsers.Any(id => IsAll(id) || Same(id, user.Id) || (isGuest && Same(id, GuestsOrExternalUsers))) ||
            policy.IncludeGroups.Any(groupIds.Contains) ||
            policy.IncludeRoles.Any(roleIds.Contains))
        {
            included = Scope.Full;
        }
        else
        {
            var includedTypes = policy.IncludeGuestTypes & guestTypes;
            if (includedTypes != GuestOrExternalUserTypes.None)
            {
                included = includedTypes == guestTypes && !policy.IncludeGuestsLimitedToTenants
                    ? Scope.Full
                    : Scope.Partial;
            }
        }

        return included < cap ? included : cap;
    }

    private static bool IsMfa(string control) => Same(control, MfaControl);

    private static bool IsAll(string value) => Same(value, All);

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
