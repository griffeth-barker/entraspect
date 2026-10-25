namespace Entraspect.Core;

/// <summary>Where a guest's invitation stands. Graph leaves it out for users who weren't invited, such as members.</summary>
public enum ExternalUserState
{
    PendingAcceptance,
    Accepted,

    /// <summary>A value entraspect doesn't know.</summary>
    Unknown,
}

/// <summary>A user in the tenant, including users synced from on-premises Active Directory.</summary>
public sealed class DirectoryUser
{
    public required string Id { get; init; }

    public required string UserPrincipalName { get; init; }

    public required string DisplayName { get; init; }

    public required UserType UserType { get; init; }

    public required bool AccountEnabled { get; init; }

    /// <summary>When the user was created, if Graph returned it.</summary>
    public DateTimeOffset? CreatedDateTime { get; init; }

    /// <summary>The invitation state of a guest invited through B2B collaboration; null when Graph has none.</summary>
    public ExternalUserState? ExternalUserState { get; init; }

    /// <summary>When <see cref="ExternalUserState"/> last changed, for example when the guest was invited.</summary>
    public DateTimeOffset? ExternalUserStateChangeDateTime { get; init; }

    /// <summary>
    /// True when the user is synced from on-premises Active Directory, where it is enabled, disabled and deleted. Null
    /// when Graph has no value, which means a cloud-only user.
    /// </summary>
    public bool? OnPremisesSyncEnabled { get; init; }

    /// <summary>How many licences the user has, assigned directly or through groups; 0 when none.</summary>
    public int AssignedLicenceCount { get; init; }
}
