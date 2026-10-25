namespace Entraspect.Core;

public enum ConditionalAccessPolicyState
{
    Enabled,
    Disabled,

    /// <summary>Report-only: evaluated and logged at sign-in, but not enforced.</summary>
    ReportOnly,
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

    /// <summary>Whether the policy includes internal guests or external users through their user types.</summary>
    public bool IncludeGuestsOrExternalUsers { get; init; }

    public IReadOnlyList<string> IncludeApplications { get; init; } = [];

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
}
