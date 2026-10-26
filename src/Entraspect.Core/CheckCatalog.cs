using System.Collections.Generic;

namespace Entraspect.Core;

/// <summary>
/// The single list of every check in the app, in navigation order: within each category, sorted by title so users can
/// find a check by name. Later specs register new checks here and nowhere else, in title order.
/// </summary>
public static class CheckCatalog
{
    /// <summary>
    /// Longest allowed <see cref="ICheck.Title"/>. The title is the navigation label, so keep it short; the section
    /// already names the object type (for example "Expiring credentials" under App registrations).
    /// </summary>
    public const int MaxTitleLength = 26;

    /// <summary>Read at connect to learn the tenant's licence. No check requires it; without it the licence is unknown.</summary>
    public const string LicencePermission = "LicenseAssignment.Read.All";

    /// <summary>
    /// Permissions entraspect itself uses, outside any check. They are never reported as unused.
    /// </summary>
    public static IReadOnlyList<string> AppPermissions { get; } = [LicencePermission];

    public static IReadOnlyList<ICheck> All(TimeProvider timeProvider) =>
        new ICheck[]
        {
            new Checks.UsersDisabledAccessCheck(),
            new Checks.GuestsAdminRolesCheck(),
            new Checks.GuestsInactiveCheck(timeProvider),
            new Checks.UsersInactiveCheck(timeProvider),
            new Checks.UsersMfaNotRegisteredCheck(),
            new Checks.UsersMfaNotRequiredCheck(timeProvider),
            new Checks.GuestsPendingInvitesCheck(timeProvider),
            new Checks.UsersRiskyCheck(),
            new Checks.GroupNoMembersCheck(timeProvider),
            new Checks.GroupNoOwnersCheck(timeProvider),
            new Checks.GroupSingleOwnerCheck(timeProvider),
            new Checks.GroupLicenseErrorsCheck(),
            new Checks.GroupDynamicPausedCheck(),
            new Checks.GroupPublicCheck(),
            new Checks.GroupSyncErrorsCheck(),
            new Checks.AppNoOwnersCheck(),
            new Checks.AppCredentialExpiryCheck(timeProvider),
            new Checks.AppRiskyRedirectUrisCheck(),
            new Checks.SamlSigningCertificateExpiryCheck(timeProvider),
            new Checks.HighPrivilegeAppPermissionsCheck(),
            new Checks.RolesAdminCountsCheck(),
            new Checks.RolesLicensedAdminsCheck(),
            new Checks.RolesAdminsNoPhishingResistantMfaCheck(),
            new Checks.RolesGroupOwnersCheck(),
            new Checks.RolesSyncedAdminsCheck(),
            new Checks.ConditionalAccessPolicyHygieneCheck(timeProvider),
            new Checks.ConditionalAccessUnusedPoliciesCheck(timeProvider),
            new Checks.CrossTenantAccessDefaultsCheck(),
            new Checks.CrossTenantAccessPartnersCheck(),
            new Checks.ExternalCollaborationSettingsCheck(),
        };
}
