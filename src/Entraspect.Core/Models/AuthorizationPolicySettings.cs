namespace Entraspect.Core;

/// <summary>Who in the tenant can invite guests: the authorization policy's <c>allowInvitesFrom</c>.</summary>
public enum AllowInvitesFrom
{
    None,
    AdminsAndGuestInviters,
    AdminsGuestInvitersAndAllMembers,
    Everyone,

    /// <summary>Missing, or a value entraspect doesn't know; the raw value is in <see cref="AuthorizationPolicySettings.AllowInvitesFromRaw"/>.</summary>
    Unknown,
}

/// <summary>The guest settings of the tenant's authorization policy.</summary>
public sealed class AuthorizationPolicySettings
{
    /// <summary>Role template ID that sets what guests can read in the directory. Empty when Graph returned none.</summary>
    public string GuestUserRoleId { get; init; } = string.Empty;

    public AllowInvitesFrom AllowInvitesFrom { get; init; } = AllowInvitesFrom.Unknown;

    /// <summary>The <c>allowInvitesFrom</c> string Graph returned, for the message when it is unknown.</summary>
    public string? AllowInvitesFromRaw { get; init; }

    /// <summary>Whether people can join the tenant by verifying an email address at one of its domains.</summary>
    public bool? AllowEmailVerifiedUsersToJoinOrganization { get; init; }
}
