using Entraspect.Core.Checks;
using static Entraspect.Core.Tests.Checks.CrossTenantAccessTestData;

namespace Entraspect.Core.Tests.Checks;

public sealed class CrossTenantAccessDefaultsCheckTests
{
    private static readonly CrossTenantAccess LockedCollaboration = Access(AllowedSome, AllowedAll);

    private static Task<IReadOnlyList<Finding>> RunAsync(CrossTenantAccessSettings defaults) =>
        new CrossTenantAccessDefaultsCheck().RunAsync(
            new FakeDirectoryReader(Array.Empty<AppRegistration>()) { CrossTenantDefaults = defaults },
            new CheckOptions());

    private static CrossTenantAccessSettings Locked() => new()
    {
        InboundTrust = new CrossTenantInboundTrust(),
        B2bCollaborationInbound = LockedCollaboration,
        B2bCollaborationOutbound = LockedCollaboration,
        B2bDirectConnectInbound = Blocked,
        B2bDirectConnectOutbound = Blocked,
    };

    [Fact]
    public async Task Service_defaults_report_b2b_collaboration_open_to_all_in_both_directions()
    {
        var findings = await RunAsync(ServiceDefaults());

        Assert.Equal(2, findings.Count);
        Assert.All(findings, f =>
        {
            Assert.Equal("cross-tenant-access-defaults", f.CheckId);
            Assert.Equal(Severity.Info, f.Severity);
            Assert.Equal("CrossTenantAccessPolicy", f.ObjectType);
            Assert.Equal("Allowed for all", f.Value);
        });
        Assert.Equal(["B2B collaboration: Inbound", "B2B collaboration: Outbound"], findings.Select(f => f.ObjectDisplayName));
        Assert.Equal(["b2bCollaborationInbound", "b2bCollaborationOutbound"], findings.Select(f => f.ObjectId));
        Assert.Equal(
            "Users from every external tenant can be invited. This is the default; consider allowing only the " +
            "organisations you work with.",
            findings[0].Message);
        Assert.Equal(
            "Your users can be invited by every external tenant. This is the default; consider allowing only the " +
            "organisations you work with.",
            findings[1].Message);
    }

    [Fact]
    public async Task Locked_down_or_unread_defaults_have_no_finding()
    {
        Assert.Empty(await RunAsync(Locked()));
        Assert.Empty(await RunAsync(new CrossTenantAccessSettings()));
    }

    [Fact]
    public async Task Trusting_mfa_from_every_tenant_is_info()
    {
        var f = Assert.Single(await RunAsync(new CrossTenantAccessSettings
        {
            InboundTrust = new CrossTenantInboundTrust { IsMfaAccepted = true },
        }));

        Assert.Equal(Severity.Info, f.Severity);
        Assert.Equal("Inbound trust: MFA", f.ObjectDisplayName);
        Assert.Equal("inboundTrust.isMfaAccepted", f.ObjectId);
        Assert.Equal("On", f.Value);
        Assert.Equal(
            "MFA done in any external tenant satisfies your MFA policies. Consider trusting MFA only for partner " +
            "organisations.",
            f.Message);
    }

    [Fact]
    public async Task Trusting_device_claims_from_every_tenant_is_a_warning_per_claim()
    {
        var findings = await RunAsync(new CrossTenantAccessSettings
        {
            InboundTrust = new CrossTenantInboundTrust { IsCompliantDeviceAccepted = true, IsHybridJoinedDeviceAccepted = true },
        });

        Assert.Equal(
            ["Inbound trust: Compliant device", "Inbound trust: Hybrid joined device"],
            findings.Select(f => f.ObjectDisplayName));
        Assert.Equal(
            ["inboundTrust.isCompliantDeviceAccepted", "inboundTrust.isHybridAzureADJoinedDeviceAccepted"],
            findings.Select(f => f.ObjectId));
        Assert.All(findings, f =>
        {
            Assert.Equal(Severity.Warning, f.Severity);
            Assert.Equal(CrossTenantAccessDefaultsCheck.DeviceTrustMessage, f.Message);
        });
    }

    [Fact]
    public async Task Automatic_redemption_for_every_tenant_is_a_warning_per_direction()
    {
        var findings = await RunAsync(new CrossTenantAccessSettings
        {
            AutomaticRedemptionInbound = true,
            AutomaticRedemptionOutbound = true,
        });

        Assert.Equal(
            ["Automatic redemption: Inbound", "Automatic redemption: Outbound"],
            findings.Select(f => f.ObjectDisplayName));
        Assert.Equal(
            ["automaticUserConsentSettings.inboundAllowed", "automaticUserConsentSettings.outboundAllowed"],
            findings.Select(f => f.ObjectId));
        Assert.All(findings, f =>
        {
            Assert.Equal(Severity.Warning, f.Severity);
            Assert.Equal("On", f.Value);
            Assert.Equal(CrossTenantAccessDefaultsCheck.RedemptionMessage, f.Message);
        });
    }

    [Fact]
    public async Task Direct_connect_open_to_every_tenant_is_a_warning_with_how_far_it_is_open()
    {
        var defaults = Locked();
        var findings = await RunAsync(new CrossTenantAccessSettings
        {
            B2bCollaborationInbound = defaults.B2bCollaborationInbound,
            B2bCollaborationOutbound = defaults.B2bCollaborationOutbound,
            B2bDirectConnectInbound = OpenToAll,
            B2bDirectConnectOutbound = Access(BlockedSome, AllowedAll),
        });

        Assert.Equal(["B2B direct connect: Inbound", "B2B direct connect: Outbound"], findings.Select(f => f.ObjectDisplayName));
        Assert.Equal(["b2bDirectConnectInbound", "b2bDirectConnectOutbound"], findings.Select(f => f.ObjectId));
        Assert.Equal(["Allowed for all", "Allowed for some"], findings.Select(f => f.Value));
        Assert.All(findings, f =>
        {
            Assert.Equal(Severity.Warning, f.Severity);
            Assert.Equal(CrossTenantAccessDefaultsCheck.DirectConnectMessage, f.Message);
        });
    }

    [Fact]
    public async Task Direct_connect_blocked_for_all_users_or_all_applications_has_no_finding()
    {
        Assert.Empty(await RunAsync(new CrossTenantAccessSettings
        {
            B2bDirectConnectInbound = Access(BlockedAll, AllowedAll),
            B2bDirectConnectOutbound = Access(AllowedAll, BlockedAll),
        }));
    }

    [Fact]
    public async Task B2b_collaboration_allowed_for_some_has_no_finding()
    {
        Assert.Empty(await RunAsync(new CrossTenantAccessSettings
        {
            B2bCollaborationInbound = Access(AllowedAll, AllowedSome),
            B2bCollaborationOutbound = Access(BlockedSome, AllowedAll),
        }));
    }

    [Fact]
    public async Task Failed_read_throws_for_the_scan_to_report()
    {
        var reader = new FakeDirectoryReader(Array.Empty<AppRegistration>()) { CrossTenantDefaultsFailure = new DirectoryReadException("denied", 403) };

        await Assert.ThrowsAsync<DirectoryReadException>(
            () => new CrossTenantAccessDefaultsCheck().RunAsync(reader, new CheckOptions()));
    }

    [Fact]
    public void Shows_setting_property_and_value_columns()
    {
        var check = new CrossTenantAccessDefaultsCheck();

        Assert.Equal(CheckCategory.ExternalIdentities, check.Category);
        Assert.Equal(["Policy.Read.All"], check.RequiredPermissions);
        Assert.Equal("Setting", check.Labels.ObjectDisplayName);
        Assert.Equal("Graph property", check.Labels.ObjectId);
        Assert.Equal("Current value", check.Labels.Value);
        Assert.Null(check.Labels.SubObjectDisplayName);
        Assert.Null(check.Labels.DueDate);
    }
}
