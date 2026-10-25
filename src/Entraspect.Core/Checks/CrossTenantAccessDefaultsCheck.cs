using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using static Entraspect.Core.Checks.ExternalIdentitiesRules;

namespace Entraspect.Core.Checks;

/// <summary>
/// The default cross-tenant access settings, which apply to every Entra organisation that isn't a partner: trust,
/// automatic redemption and B2B direct connect opened to everyone, and B2B collaboration left open to all.
/// </summary>
public sealed class CrossTenantAccessDefaultsCheck : ICheck
{
    public const string RedemptionMessage =
        "Invitations are redeemed without a consent prompt for every external tenant. Turn this on only for partner " +
        "organisations.";

    public const string DeviceTrustMessage =
        "Device claims from any external tenant are trusted. Trust device claims only from partner organisations you " +
        "manage devices with.";

    public const string DirectConnectMessage =
        "B2B direct connect is open to every external tenant. Allow it only for partner organisations.";

    public string Id => "cross-tenant-access-defaults";

    public string Title => "Cross-tenant defaults";

    public string Description =>
        "Reviews the default cross-tenant access settings, which apply to every Entra organisation you haven't added " +
        "as a partner. Trust, automatic redemption and B2B direct connect belong on known partners, not on the " +
        "defaults. In the Entra admin center: External Identities > Cross-tenant access settings > Default settings.";

    public CheckCategory Category => CheckCategory.ExternalIdentities;

    public IReadOnlyList<string> RequiredPermissions => new[] { PolicyPermission };

    public FindingLabels Labels => SettingLabels;

    public async Task<IReadOnlyList<Finding>> RunAsync(
        IDirectoryReader reader,
        CheckOptions options,
        CancellationToken cancellationToken = default)
    {
        var defaults = await reader.GetCrossTenantAccessDefaultsAsync(cancellationToken).ConfigureAwait(false);
        var findings = new List<Finding>();

        void AddIfOn(bool? setting, Severity severity, string name, string property, string message)
        {
            if (setting == true)
            {
                findings.Add(Setting(severity, name, property, On, message));
            }
        }

        AddIfOn(defaults.InboundTrust.IsMfaAccepted, Severity.Info, TrustMfa, "inboundTrust.isMfaAccepted",
            "MFA done in any external tenant satisfies your MFA policies. Consider trusting MFA only for partner " +
            "organisations.");
        AddIfOn(defaults.InboundTrust.IsCompliantDeviceAccepted, Severity.Warning, TrustCompliantDevice,
            "inboundTrust.isCompliantDeviceAccepted", DeviceTrustMessage);
        AddIfOn(defaults.InboundTrust.IsHybridJoinedDeviceAccepted, Severity.Warning, TrustHybridJoinedDevice,
            "inboundTrust.isHybridAzureADJoinedDeviceAccepted", DeviceTrustMessage);
        AddIfOn(defaults.AutomaticRedemptionInbound, Severity.Warning, RedemptionInbound,
            "automaticUserConsentSettings.inboundAllowed", RedemptionMessage);
        AddIfOn(defaults.AutomaticRedemptionOutbound, Severity.Warning, RedemptionOutbound,
            "automaticUserConsentSettings.outboundAllowed", RedemptionMessage);

        AddDirectConnect(findings, defaults.B2bDirectConnectInbound, DirectConnectInbound, "b2bDirectConnectInbound");
        AddDirectConnect(findings, defaults.B2bDirectConnectOutbound, DirectConnectOutbound, "b2bDirectConnectOutbound");

        if (defaults.B2bCollaborationInbound is { IsOpenToAll: true })
        {
            findings.Add(Setting(Severity.Info, CollaborationInbound, "b2bCollaborationInbound", AllowedForAll,
                "Users from every external tenant can be invited. This is the default; consider allowing only the " +
                "organisations you work with."));
        }

        if (defaults.B2bCollaborationOutbound is { IsOpenToAll: true })
        {
            findings.Add(Setting(Severity.Info, CollaborationOutbound, "b2bCollaborationOutbound", AllowedForAll,
                "Your users can be invited by every external tenant. This is the default; consider allowing only " +
                "the organisations you work with."));
        }

        return findings;
    }

    private void AddDirectConnect(List<Finding> findings, CrossTenantAccess? access, string name, string property)
    {
        if (access is { IsAllowedForAny: true })
        {
            findings.Add(Setting(Severity.Warning, name, property, AccessValue(access), DirectConnectMessage));
        }
    }

    private Finding Setting(Severity severity, string name, string property, string value, string message) => new()
    {
        CheckId = Id,
        Severity = severity,
        ObjectType = CrossTenantAccessPolicyType,
        ObjectId = property,
        ObjectDisplayName = name,
        Value = value,
        Message = message,
    };
}
