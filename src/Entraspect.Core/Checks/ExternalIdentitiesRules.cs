namespace Entraspect.Core.Checks;

/// <summary>Names, Graph properties and value text shared by the External Identities checks.</summary>
internal static class ExternalIdentitiesRules
{
    public const string AuthorizationPolicyType = "AuthorizationPolicy";

    public const string CrossTenantAccessPolicyType = "CrossTenantAccessPolicy";

    public const string PolicyPermission = "Policy.Read.All";

    public const string On = "On";

    public const string AllowedForAll = "Allowed for all";

    public const string AllowedForSome = "Allowed for some";

    // Settings checks: the setting as the admin center names it, its Graph property and its current value.
    public static readonly FindingLabels SettingLabels = new()
    {
        ObjectDisplayName = "Setting",
        ObjectId = "Graph property",
        SubObjectDisplayName = null,
        SubObjectType = null,
        DueDate = null,
        Value = "Current value",
    };

    // Setting names, with the direction first, used by both cross-tenant checks.
    public const string TrustMfa = "Inbound trust: MFA";
    public const string TrustCompliantDevice = "Inbound trust: Compliant device";
    public const string TrustHybridJoinedDevice = "Inbound trust: Hybrid joined device";
    public const string RedemptionInbound = "Automatic redemption: Inbound";
    public const string RedemptionOutbound = "Automatic redemption: Outbound";
    public const string DirectConnectInbound = "B2B direct connect: Inbound";
    public const string DirectConnectOutbound = "B2B direct connect: Outbound";
    public const string CollaborationInbound = "B2B collaboration: Inbound";
    public const string CollaborationOutbound = "B2B collaboration: Outbound";

    /// <summary>"Allowed for all" when every user and application is allowed, otherwise "Allowed for some".</summary>
    public static string AccessValue(CrossTenantAccess access) => access.IsOpenToAll ? AllowedForAll : AllowedForSome;
}
