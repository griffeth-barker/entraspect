namespace Entraspect.Core;

public enum ConditionalAccessPolicyState
{
    Enabled,
    Disabled,

    /// <summary>Report-only: evaluated and logged at sign-in, but not enforced.</summary>
    ReportOnly,
}

/// <summary>The kinds of guest and external user a policy includes or excludes, as Conditional Access names them.</summary>
[Flags]
public enum GuestOrExternalUserTypes
{
    None = 0,
    InternalGuest = 1,
    B2bCollaborationGuest = 2,
    B2bCollaborationMember = 4,
    B2bDirectConnectUser = 8,
    OtherExternalUser = 16,
    ServiceProvider = 32,
}

/// <summary>How a policy's grant controls combine: the user must satisfy all of them, or any one.</summary>
public enum GrantOperator
{
    And,
    Or,
}

/// <summary>
/// A Conditional Access policy, with the users, groups, roles and apps it includes or excludes. The ID lists hold
/// object IDs (users and groups), role template IDs (roles) or app IDs (applications), and may also hold special
/// values that aren't object IDs, such as <c>All</c>, <c>None</c>, <c>GuestsOrExternalUsers</c>, <c>Office365</c> and
/// <c>MicrosoftAdminPortals</c>.
/// </summary>
public sealed class ConditionalAccessPolicy
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required ConditionalAccessPolicyState State { get; init; }

    public DateTimeOffset? CreatedDateTime { get; init; }

    /// <summary>When the policy was last changed; empty if Graph doesn't return it.</summary>
    public DateTimeOffset? ModifiedDateTime { get; init; }

    public IReadOnlyList<string> IncludeUsers { get; init; } = [];

    public IReadOnlyList<string> ExcludeUsers { get; init; } = [];

    public IReadOnlyList<string> IncludeGroups { get; init; } = [];

    public IReadOnlyList<string> ExcludeGroups { get; init; } = [];

    public IReadOnlyList<string> IncludeRoles { get; init; } = [];

    public IReadOnlyList<string> ExcludeRoles { get; init; } = [];

    /// <summary>The guest and external user types the policy includes.</summary>
    public GuestOrExternalUserTypes IncludeGuestTypes { get; init; }

    /// <summary>Whether <see cref="IncludeGuestTypes"/> applies only to users from specific external tenants.</summary>
    public bool IncludeGuestsLimitedToTenants { get; init; }

    /// <summary>The guest and external user types the policy excludes.</summary>
    public GuestOrExternalUserTypes ExcludeGuestTypes { get; init; }

    /// <summary>Whether <see cref="ExcludeGuestTypes"/> applies only to users from specific external tenants.</summary>
    public bool ExcludeGuestsLimitedToTenants { get; init; }

    /// <summary>Whether the policy includes internal guests or external users through their user types.</summary>
    public bool IncludeGuestsOrExternalUsers => IncludeGuestTypes != GuestOrExternalUserTypes.None;

    public IReadOnlyList<string> IncludeApplications { get; init; } = [];

    public IReadOnlyList<string> ExcludeApplications { get; init; } = [];

    /// <summary>Whether the policy excludes apps through an application filter (custom security attributes).</summary>
    public bool ExcludeApplicationsByFilter { get; init; }

    /// <summary>
    /// Whether the policy includes apps through an application filter (custom security attributes) instead of, or as
    /// well as, <see cref="IncludeApplications"/>.
    /// </summary>
    public bool IncludeApplicationsByFilter { get; init; }

    /// <summary>User actions, for example <c>urn:user:registersecurityinfo</c>.</summary>
    public IReadOnlyList<string> IncludeUserActions { get; init; } = [];

    /// <summary>Authentication context class references, for example <c>c1</c>.</summary>
    public IReadOnlyList<string> IncludeAuthenticationContexts { get; init; } = [];

    /// <summary>Whether the policy targets workload identities (service principals) through its client applications.</summary>
    public bool TargetsServicePrincipals { get; init; }

    /// <summary>
    /// The grant controls Conditional Access names as built in, for example <c>mfa</c>, <c>compliantDevice</c> or
    /// <c>block</c>, in Graph's spelling.
    /// </summary>
    public IReadOnlyList<string> GrantBuiltInControls { get; init; } = [];

    public GrantOperator GrantOperator { get; init; } = GrantOperator.Or;

    /// <summary>Whether the policy requires an authentication strength.</summary>
    public bool HasAuthenticationStrength { get; init; }

    /// <summary>
    /// Whether the policy applies only under some conditions: it sets locations, device platforms, user, sign-in or
    /// insider risk levels, a device filter, authentication flows, or client app types other than all.
    /// </summary>
    public bool HasNarrowingConditions { get; init; }
}
