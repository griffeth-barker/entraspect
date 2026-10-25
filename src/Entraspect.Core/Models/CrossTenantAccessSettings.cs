namespace Entraspect.Core;

/// <summary>
/// Cross-tenant access settings: the defaults, which apply to every other Entra organisation, or the settings for one
/// partner organisation. On a partner, a null property isn't set and inherits the defaults.
/// </summary>
public sealed class CrossTenantAccessSettings
{
    /// <summary>The partner's tenant ID; null for the defaults.</summary>
    public string? TenantId { get; init; }

    /// <summary>Whether the defaults are still Microsoft's service defaults. Defaults only.</summary>
    public bool? IsServiceDefault { get; init; }

    /// <summary>Whether the partner is marked as a service provider. Partners only.</summary>
    public bool? IsServiceProvider { get; init; }

    public CrossTenantInboundTrust InboundTrust { get; init; } = new();

    /// <summary>Invitations from the other tenant are redeemed without a consent prompt.</summary>
    public bool? AutomaticRedemptionInbound { get; init; }

    /// <summary>Invitations to the other tenant are redeemed without a consent prompt.</summary>
    public bool? AutomaticRedemptionOutbound { get; init; }

    public CrossTenantAccess? B2bCollaborationInbound { get; init; }

    public CrossTenantAccess? B2bCollaborationOutbound { get; init; }

    public CrossTenantAccess? B2bDirectConnectInbound { get; init; }

    public CrossTenantAccess? B2bDirectConnectOutbound { get; init; }
}

/// <summary>Which claims from the other tenant's sign-ins are trusted. Null on a partner means it inherits the defaults.</summary>
public sealed class CrossTenantInboundTrust
{
    public bool? IsMfaAccepted { get; init; }

    public bool? IsCompliantDeviceAccepted { get; init; }

    public bool? IsHybridJoinedDeviceAccepted { get; init; }
}

/// <summary>Who and which applications a B2B collaboration or B2B direct connect setting allows or blocks.</summary>
public sealed class CrossTenantAccess
{
    /// <summary>Users and groups; null when Graph returned none.</summary>
    public CrossTenantAccessTarget? Users { get; init; }

    /// <summary>Applications; null when Graph returned none.</summary>
    public CrossTenantAccessTarget? Applications { get; init; }

    /// <summary>All users and all applications are allowed.</summary>
    public bool IsOpenToAll => Users is { AccessType: CrossTenantAccessType.Allowed, IncludesAll: true }
        && Applications is { AccessType: CrossTenantAccessType.Allowed, IncludesAll: true };

    /// <summary>
    /// Some user can use some application: neither side is blocked for all its targets. False when either side is
    /// missing or unknown, since it can't be told.
    /// </summary>
    public bool IsAllowedForAny => IsKnown(Users) && IsKnown(Applications)
        && !Users!.IsBlockedForAll && !Applications!.IsBlockedForAll;

    private static bool IsKnown(CrossTenantAccessTarget? target) =>
        target is not null && target.AccessType != CrossTenantAccessType.Unknown;
}

/// <summary>Whether the targets are allowed or blocked, and whether they are all users or all applications.</summary>
/// <param name="IncludesAll">The targets include <c>AllUsers</c> or <c>AllApplications</c>.</param>
public sealed record CrossTenantAccessTarget(CrossTenantAccessType AccessType, bool IncludesAll)
{
    public bool IsBlockedForAll => AccessType == CrossTenantAccessType.Blocked && IncludesAll;
}

public enum CrossTenantAccessType
{
    Allowed,
    Blocked,

    /// <summary>Missing, or a value entraspect doesn't know.</summary>
    Unknown,
}
