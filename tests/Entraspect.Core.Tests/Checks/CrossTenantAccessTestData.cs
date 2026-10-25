namespace Entraspect.Core.Tests.Checks;

/// <summary>Cross-tenant access settings for tests, starting from Microsoft's service defaults.</summary>
internal static class CrossTenantAccessTestData
{
    public static readonly CrossTenantAccessTarget AllowedAll = new(CrossTenantAccessType.Allowed, IncludesAll: true);
    public static readonly CrossTenantAccessTarget AllowedSome = new(CrossTenantAccessType.Allowed, IncludesAll: false);
    public static readonly CrossTenantAccessTarget BlockedAll = new(CrossTenantAccessType.Blocked, IncludesAll: true);
    public static readonly CrossTenantAccessTarget BlockedSome = new(CrossTenantAccessType.Blocked, IncludesAll: false);

    public static CrossTenantAccess Access(CrossTenantAccessTarget users, CrossTenantAccessTarget applications) =>
        new() { Users = users, Applications = applications };

    public static CrossTenantAccess OpenToAll => Access(AllowedAll, AllowedAll);

    public static CrossTenantAccess Blocked => Access(BlockedAll, BlockedAll);

    /// <summary>
    /// What a tenant gets out of the box: no trust, no automatic redemption, B2B collaboration open to all and B2B
    /// direct connect blocked.
    /// </summary>
    public static CrossTenantAccessSettings ServiceDefaults() => new()
    {
        IsServiceDefault = true,
        InboundTrust = new CrossTenantInboundTrust
        {
            IsMfaAccepted = false,
            IsCompliantDeviceAccepted = false,
            IsHybridJoinedDeviceAccepted = false,
        },
        AutomaticRedemptionInbound = false,
        AutomaticRedemptionOutbound = false,
        B2bCollaborationInbound = OpenToAll,
        B2bCollaborationOutbound = OpenToAll,
        B2bDirectConnectInbound = Blocked,
        B2bDirectConnectOutbound = Blocked,
    };
}
