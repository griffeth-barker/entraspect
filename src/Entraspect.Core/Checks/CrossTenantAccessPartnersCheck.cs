using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using static Entraspect.Core.Checks.ExternalIdentitiesRules;

namespace Entraspect.Core.Checks;

/// <summary>
/// Partner organisations' cross-tenant access settings worth reviewing: one Info finding per trusted claim, automatic
/// redemption, B2B direct connect setting and service provider mark. A setting a partner inherits is left to
/// <see cref="CrossTenantAccessDefaultsCheck"/>.
/// </summary>
public sealed class CrossTenantAccessPartnersCheck : ICheck
{
    public string Id => "cross-tenant-access-partners";

    public string Title => "Cross-tenant partners";

    public string Description =>
        "Lists partner organisations' cross-tenant access settings worth reviewing: trusted MFA or device claims, " +
        "automatic redemption, B2B direct connect and service providers. Partner settings are deliberate, so each " +
        "finding is Info, for review. Settings a partner inherits are covered by Cross-tenant defaults. In the Entra " +
        "admin center: External Identities > Cross-tenant access settings > Organizational settings.";

    public CheckCategory Category => CheckCategory.ExternalIdentities;

    public IReadOnlyList<string> RequiredPermissions => new[] { PolicyPermission };

    // Graph returns only the partner's tenant ID, so it names the partner.
    public FindingLabels Labels { get; } = new()
    {
        ObjectDisplayName = "Tenant ID",
        ObjectId = null,
        SubObjectDisplayName = "Setting",
        SubObjectType = null,
        DueDate = null,
        Value = "Current value",
    };

    public async Task<IReadOnlyList<Finding>> RunAsync(
        IDirectoryReader reader,
        CheckOptions options,
        CancellationToken cancellationToken = default)
    {
        var partners = await reader.GetCrossTenantAccessPartnersAsync(cancellationToken).ConfigureAwait(false);
        var findings = new List<Finding>();

        foreach (var partner in partners)
        {
            var tenantId = partner.TenantId ?? string.Empty;

            void AddIfOn(bool? setting, string name, string message)
            {
                if (setting == true)
                {
                    findings.Add(Setting(tenantId, name, On, message));
                }
            }

            void AddIfAllowed(CrossTenantAccess? access, string name, string message)
            {
                if (access is { IsAllowedForAny: true })
                {
                    findings.Add(Setting(tenantId, name, AccessValue(access), message));
                }
            }

            AddIfOn(partner.InboundTrust.IsMfaAccepted, TrustMfa, "This organisation's MFA claims are trusted.");
            AddIfOn(partner.InboundTrust.IsCompliantDeviceAccepted, TrustCompliantDevice,
                "This organisation's compliant device claims are trusted.");
            AddIfOn(partner.InboundTrust.IsHybridJoinedDeviceAccepted, TrustHybridJoinedDevice,
                "This organisation's hybrid joined device claims are trusted.");
            AddIfOn(partner.AutomaticRedemptionInbound, RedemptionInbound,
                "Users from this organisation redeem your invitations without a consent prompt.");
            AddIfOn(partner.AutomaticRedemptionOutbound, RedemptionOutbound,
                "Your users redeem this organisation's invitations without a consent prompt.");
            AddIfAllowed(partner.B2bDirectConnectInbound, DirectConnectInbound,
                "Users from this organisation can use B2B direct connect with your tenant.");
            AddIfAllowed(partner.B2bDirectConnectOutbound, DirectConnectOutbound,
                "Your users can use B2B direct connect with this organisation.");
            AddIfOn(partner.IsServiceProvider, "Service provider",
                "Marked as a service provider (for example a Microsoft partner with delegated admin).");
        }

        return findings;
    }

    private Finding Setting(string tenantId, string name, string value, string message) => new()
    {
        CheckId = Id,
        Severity = Severity.Info,
        ObjectType = CrossTenantAccessPolicyType,
        ObjectId = tenantId,
        ObjectDisplayName = tenantId,
        SubObjectDisplayName = name,
        Value = value,
        Message = message,
    };
}
