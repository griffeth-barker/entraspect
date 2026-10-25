namespace Entraspect.Core;

/// <summary>How likely Microsoft Entra ID Protection thinks it is that a user is compromised.</summary>
public enum RiskLevel
{
    Low,
    Medium,
    High,

    /// <summary>Microsoft doesn't show the level, for example to a tenant without Entra ID P2.</summary>
    Hidden,

    None,

    /// <summary>A missing value, or one entraspect doesn't know.</summary>
    Unknown,
}

/// <summary>Where a risky user stands: still at risk, or dealt with by an admin or the user.</summary>
public enum RiskState
{
    AtRisk,
    ConfirmedCompromised,
    Remediated,
    Dismissed,
    ConfirmedSafe,
    None,

    /// <summary>A missing value, or one entraspect doesn't know.</summary>
    Unknown,
}

/// <summary>A user, member or guest, that Microsoft Entra ID Protection rates.</summary>
public sealed class RiskyUser
{
    /// <summary>The user's object ID.</summary>
    public required string Id { get; init; }

    public required string UserPrincipalName { get; init; }

    public required string DisplayName { get; init; }

    public required RiskLevel RiskLevel { get; init; }

    public required RiskState RiskState { get; init; }

    /// <summary>
    /// Why the user is in this state, as Graph names it, for example <c>adminGeneratedTemporaryPassword</c>. May be
    /// empty.
    /// </summary>
    public string RiskDetail { get; init; } = string.Empty;

    /// <summary>When the user's risk last changed, if Graph returned it.</summary>
    public DateTimeOffset? RiskLastUpdated { get; init; }

    /// <summary>Whether the user has been deleted.</summary>
    public bool IsDeleted { get; init; }
}
