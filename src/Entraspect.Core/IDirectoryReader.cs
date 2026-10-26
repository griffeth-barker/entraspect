using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Entraspect.Core;

public interface IDirectoryReader
{
    Task<IReadOnlyList<AppRegistration>> GetAppRegistrationsAsync(CancellationToken cancellationToken = default);

    /// <summary>Every group, with whether it has direct members and how many owners.</summary>
    Task<IReadOnlyList<DirectoryGroup>> GetGroupsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The IDs of the groups with members who have licence assignment errors from group-based licensing. Separate from
    /// <see cref="GetGroupsAsync"/> so that a failure here fails only the check that reads it.
    /// </summary>
    Task<IReadOnlyList<string>> GetGroupIdsWithLicenseErrorsAsync(CancellationToken cancellationToken = default);

    /// <summary>Every enterprise app (service principal), with its token signing certificates.</summary>
    Task<IReadOnlyList<EnterpriseApp>> GetEnterpriseAppsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Users' MFA registration and registered methods from the user registration details report: every user it lists,
    /// MFA capable or not. The report leaves out disabled users, and may leave out others.
    /// </summary>
    Task<IReadOnlyList<UserRegistration>> GetUserRegistrationsAsync(CancellationToken cancellationToken = default);

    /// <summary>Every Microsoft Graph application permission granted to a service principal in the tenant.</summary>
    Task<IReadOnlyList<AppPermissionGrant>> GetGraphApplicationPermissionGrantsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Every Conditional Access policy, whatever its state.</summary>
    Task<IReadOnlyList<ConditionalAccessPolicy>> GetConditionalAccessPoliciesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The IDs in <paramref name="userIds"/> that don't belong to a user in the tenant, for example because the user
    /// was deleted. An implementation may look up each user on its own, so it reports through
    /// <paramref name="progress"/> how many of the IDs it has looked up so far.
    /// </summary>
    Task<IReadOnlyList<string>> GetMissingUserIdsAsync(
        IReadOnlyCollection<string> userIds,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Every user, members and guests, cloud-only and synced from on-premises.</summary>
    Task<IReadOnlyList<DirectoryUser>> GetUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>Every active directory role assignment, at any scope. Eligible (PIM) assignments aren't included.</summary>
    Task<IReadOnlyList<DirectoryRoleAssignment>> GetDirectoryRoleAssignmentsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The IDs of the users who are members of the group, directly or through nested groups. Empty if the object
    /// doesn't exist or isn't a group, so callers can pass any directory object ID.
    /// </summary>
    Task<IReadOnlyList<string>> GetGroupTransitiveUserIdsAsync(
        string groupId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The group's owners, users and other kinds. Empty if the group doesn't exist, for example because it was deleted
    /// since it was listed.
    /// </summary>
    Task<IReadOnlyList<GroupOwner>> GetGroupOwnersAsync(string groupId, CancellationToken cancellationToken = default);

    /// <summary>Whether security defaults are turned on for the tenant.</summary>
    Task<bool> GetSecurityDefaultsEnabledAsync(CancellationToken cancellationToken = default);

    /// <summary>The user's legacy per-user MFA state. One read per user.</summary>
    Task<PerUserMfaState> GetPerUserMfaStateAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Successful single-factor interactive sign-ins since <paramref name="since"/>, by user ID. Users with none aren't
    /// in the result.
    /// </summary>
    Task<IReadOnlyDictionary<string, SingleFactorSignIns>> GetSingleFactorSignInsAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// How Conditional Access policies applied to the interactive sign-ins since <paramref name="since"/>. An
    /// implementation reads the sign-ins a page at a time and reports through <paramref name="signInsRead"/> how many
    /// it has read so far.
    /// </summary>
    Task<ConditionalAccessUsage> GetConditionalAccessUsageAsync(
        DateTimeOffset since,
        IProgress<int>? signInsRead = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The sign-in activity of every user, members and guests. Separate from <see cref="GetUsersAsync"/> because it
    /// needs an Entra ID P1 or P2 licence, so that a failure here fails only the checks that read it.
    /// </summary>
    Task<IReadOnlyList<UserSignInActivity>> GetUserSignInActivityAsync(CancellationToken cancellationToken = default);

    /// <summary>Every directory role definition, built-in and custom.</summary>
    Task<IReadOnlyList<DirectoryRoleDefinition>> GetDirectoryRoleDefinitionsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>The guest settings of the tenant's authorization policy.</summary>
    Task<AuthorizationPolicySettings> GetAuthorizationPolicyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The default cross-tenant access settings. Separate from <see cref="GetCrossTenantAccessPartnersAsync"/> so that
    /// a failure here fails only the check that reads it.
    /// </summary>
    Task<CrossTenantAccessSettings> GetCrossTenantAccessDefaultsAsync(CancellationToken cancellationToken = default);

    /// <summary>The cross-tenant access settings of every partner organisation.</summary>
    Task<IReadOnlyList<CrossTenantAccessSettings>> GetCrossTenantAccessPartnersAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The users Microsoft Entra ID Protection rates at risk or an admin confirmed compromised. An implementation may
    /// also return users in other risk states; callers must still test <see cref="RiskyUser.RiskState"/>.
    /// </summary>
    Task<IReadOnlyList<RiskyUser>> GetRiskyUsersAsync(CancellationToken cancellationToken = default);
}
