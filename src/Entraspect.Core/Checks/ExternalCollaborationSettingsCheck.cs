using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using static Entraspect.Core.Checks.ExternalIdentitiesRules;

namespace Entraspect.Core.Checks;

/// <summary>
/// The tenant's guest settings from the authorization policy: what guests can read in the directory, who can invite
/// guests, and whether people can join by verifying an email address.
/// </summary>
public sealed class ExternalCollaborationSettingsCheck : ICheck
{
    /// <summary>Guests have the same access to directory data as members.</summary>
    public const string MemberRoleId = "a0b1b346-4d3e-4e8b-98f8-753987be4970";

    /// <summary>Guests have limited access: the default.</summary>
    public const string GuestRoleId = "10dae51f-b6af-4016-8d66-8c2a99b929b3";

    /// <summary>Guests see only their own directory objects.</summary>
    public const string RestrictedGuestRoleId = "2af84b1e-32c8-42b7-82bc-daa82404023b";

    public string Id => "external-collaboration-settings";

    public string Title => "External collaboration";

    public string Description =>
        "Reviews the tenant's guest settings: what guests can read in the directory, who can invite guests, and " +
        "whether people can join without an invitation. Guest user access and guest invite restrictions are in the " +
        "Entra admin center under External Identities > External collaboration settings. Email-verified users can " +
        "join isn't on that page; it is set only through Microsoft Graph or PowerShell.";

    public CheckCategory Category => CheckCategory.ExternalIdentities;

    public IReadOnlyList<string> RequiredPermissions => new[] { PolicyPermission };

    public FindingLabels Labels => SettingLabels;

    public async Task<IReadOnlyList<Finding>> RunAsync(
        IDirectoryReader reader,
        CheckOptions options,
        CancellationToken cancellationToken = default)
    {
        var policy = await reader.GetAuthorizationPolicyAsync(cancellationToken).ConfigureAwait(false);
        var findings = new List<Finding>();

        if (GuestAccessFinding(policy.GuestUserRoleId) is { } guestAccess)
        {
            findings.Add(guestAccess);
        }

        if (InviteFinding(policy) is { } invite)
        {
            findings.Add(invite);
        }

        if (policy.AllowEmailVerifiedUsersToJoinOrganization == true)
        {
            findings.Add(Setting(
                Severity.Warning,
                "Email-verified users can join",
                "allowEmailVerifiedUsersToJoinOrganization",
                On,
                "People can join this tenant by verifying an email address at one of its domains, without an " +
                "invitation."));
        }

        return findings;
    }

    private Finding? GuestAccessFinding(string roleId)
    {
        const string name = "Guest user access";
        const string property = "guestUserRoleId";

        if (Is(roleId, RestrictedGuestRoleId))
        {
            return null;
        }

        if (Is(roleId, MemberRoleId))
        {
            return Setting(Severity.Critical, name, property, "Same as members",
                "Guests have the same access to directory data as members. Limit guest access.");
        }

        if (Is(roleId, GuestRoleId))
        {
            return Setting(Severity.Info, name, property, "Limited",
                "Guests can see other users and group memberships. Consider restricting them to their own " +
                "directory objects.");
        }

        return Setting(Severity.Info, name, property, NullIfEmpty(roleId), "Unrecognised guest access level.");
    }

    private Finding? InviteFinding(AuthorizationPolicySettings policy)
    {
        const string name = "Guest invite restrictions";
        const string property = "allowInvitesFrom";

        return policy.AllowInvitesFrom switch
        {
            AllowInvitesFrom.Everyone => Setting(Severity.Warning, name, property, "Everyone",
                "Anyone in the organisation, guests included, can invite guests. Limit invitations to admins and " +
                "the Guest Inviter role."),
            AllowInvitesFrom.AdminsGuestInvitersAndAllMembers => Setting(Severity.Info, name, property,
                "Members and admins",
                "Every member can invite guests. Consider limiting invitations to admins and the Guest Inviter role."),
            AllowInvitesFrom.Unknown => Setting(Severity.Info, name, property,
                NullIfEmpty(policy.AllowInvitesFromRaw), "Unrecognised guest invite setting."),
            _ => null,
        };
    }

    private Finding Setting(Severity severity, string name, string property, string? value, string message) => new()
    {
        CheckId = Id,
        Severity = severity,
        ObjectType = AuthorizationPolicyType,
        ObjectId = property,
        ObjectDisplayName = name,
        Value = value,
        Message = message,
    };

    private static bool Is(string roleId, string expected) =>
        string.Equals(roleId.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
