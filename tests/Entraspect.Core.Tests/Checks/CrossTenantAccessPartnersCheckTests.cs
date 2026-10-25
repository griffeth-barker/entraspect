using Entraspect.Core.Checks;
using static Entraspect.Core.Tests.Checks.CrossTenantAccessTestData;

namespace Entraspect.Core.Tests.Checks;

public sealed class CrossTenantAccessPartnersCheckTests
{
    private const string TenantA = "00000000-0000-0000-0000-00000000000a";
    private const string TenantB = "00000000-0000-0000-0000-00000000000b";

    private static Task<IReadOnlyList<Finding>> RunAsync(params CrossTenantAccessSettings[] partners) =>
        new CrossTenantAccessPartnersCheck().RunAsync(
            new FakeDirectoryReader(Array.Empty<AppRegistration>()) { CrossTenantPartners = partners },
            new CheckOptions());

    [Fact]
    public async Task Partner_that_inherits_everything_has_no_finding()
    {
        Assert.Empty(await RunAsync(new CrossTenantAccessSettings { TenantId = TenantA }));
    }

    [Fact]
    public async Task Partner_overriding_with_nothing_to_report_has_no_finding()
    {
        Assert.Empty(await RunAsync(new CrossTenantAccessSettings
        {
            TenantId = TenantA,
            IsServiceProvider = false,
            InboundTrust = new CrossTenantInboundTrust
            {
                IsMfaAccepted = false,
                IsCompliantDeviceAccepted = false,
                IsHybridJoinedDeviceAccepted = false,
            },
            AutomaticRedemptionInbound = false,
            AutomaticRedemptionOutbound = false,
            B2bCollaborationInbound = OpenToAll,
            B2bDirectConnectInbound = Blocked,
            B2bDirectConnectOutbound = Access(BlockedAll, AllowedAll),
        }));
    }

    [Fact]
    public async Task Each_trusted_claim_is_an_info_finding()
    {
        var findings = await RunAsync(new CrossTenantAccessSettings
        {
            TenantId = TenantA,
            InboundTrust = new CrossTenantInboundTrust
            {
                IsMfaAccepted = true,
                IsCompliantDeviceAccepted = true,
                IsHybridJoinedDeviceAccepted = true,
            },
        });

        Assert.Equal(
            ["Inbound trust: MFA", "Inbound trust: Compliant device", "Inbound trust: Hybrid joined device"],
            findings.Select(f => f.SubObjectDisplayName));
        Assert.Equal(
            [
                "This organisation's MFA claims are trusted.",
                "This organisation's compliant device claims are trusted.",
                "This organisation's hybrid joined device claims are trusted.",
            ],
            findings.Select(f => f.Message));
        Assert.All(findings, f =>
        {
            Assert.Equal("cross-tenant-access-partners", f.CheckId);
            Assert.Equal(Severity.Info, f.Severity);
            Assert.Equal("CrossTenantAccessPolicy", f.ObjectType);
            Assert.Equal(TenantA, f.ObjectId);
            Assert.Equal(TenantA, f.ObjectDisplayName);
            Assert.Equal("On", f.Value);
        });
    }

    [Fact]
    public async Task Automatic_redemption_direct_connect_and_service_provider_are_reported()
    {
        var findings = await RunAsync(new CrossTenantAccessSettings
        {
            TenantId = TenantA,
            IsServiceProvider = true,
            AutomaticRedemptionInbound = true,
            AutomaticRedemptionOutbound = true,
            B2bDirectConnectInbound = OpenToAll,
            B2bDirectConnectOutbound = Access(AllowedSome, AllowedSome),
        });

        Assert.Equal(
            [
                "Automatic redemption: Inbound", "Automatic redemption: Outbound", "B2B direct connect: Inbound",
                "B2B direct connect: Outbound", "Service provider",
            ],
            findings.Select(f => f.SubObjectDisplayName));
        Assert.Equal(["On", "On", "Allowed for all", "Allowed for some", "On"], findings.Select(f => f.Value));
        Assert.Equal(
            "Marked as a service provider (for example a Microsoft partner with delegated admin).",
            findings[^1].Message);
        Assert.All(findings, f => Assert.Equal(Severity.Info, f.Severity));
    }

    [Fact]
    public async Task B2b_collaboration_is_left_to_review_on_the_admin_center()
    {
        // Partners exist to open collaboration; only trust, redemption, direct connect and service providers count.
        Assert.Empty(await RunAsync(new CrossTenantAccessSettings
        {
            TenantId = TenantA,
            B2bCollaborationInbound = OpenToAll,
            B2bCollaborationOutbound = OpenToAll,
        }));
    }

    [Fact]
    public async Task Findings_name_each_partner()
    {
        var findings = await RunAsync(
            new CrossTenantAccessSettings { TenantId = TenantA, IsServiceProvider = true },
            new CrossTenantAccessSettings { TenantId = TenantB },
            new CrossTenantAccessSettings { TenantId = TenantB, AutomaticRedemptionInbound = true });

        Assert.Equal([TenantA, TenantB], findings.Select(f => f.ObjectDisplayName));
    }

    [Fact]
    public async Task Failed_read_throws_for_the_scan_to_report()
    {
        var reader = new FakeDirectoryReader(Array.Empty<AppRegistration>()) { CrossTenantPartnersFailure = new DirectoryReadException("denied", 403) };

        await Assert.ThrowsAsync<DirectoryReadException>(
            () => new CrossTenantAccessPartnersCheck().RunAsync(reader, new CheckOptions()));
    }

    [Fact]
    public async Task Each_check_fails_on_its_own_when_its_read_fails()
    {
        var checks = new ICheck[]
        {
            new CrossTenantAccessDefaultsCheck(), new CrossTenantAccessPartnersCheck(), new ExternalCollaborationSettingsCheck(),
        };
        var reader = new FakeDirectoryReader(Array.Empty<AppRegistration>())
        {
            CrossTenantDefaults = ServiceDefaults(),
            CrossTenantPartnersFailure = new DirectoryReadException("denied", 403),
        };

        var results = await new ScanRunner(
            reader, checks, ["Policy.Read.All"], new CheckOptions(), new Microsoft.Extensions.Time.Testing.FakeTimeProvider())
            .RunAsync();

        Assert.Equal(
            [CheckResultStatus.Succeeded, CheckResultStatus.Failed, CheckResultStatus.Succeeded],
            results.Select(r => r.Status));
        Assert.Equal(403, results[1].StatusCode);
    }

    [Fact]
    public void Shows_tenant_setting_and_value_columns()
    {
        var labels = new CrossTenantAccessPartnersCheck().Labels;

        Assert.Equal("Tenant ID", labels.ObjectDisplayName);
        Assert.Null(labels.ObjectId);
        Assert.Equal("Setting", labels.SubObjectDisplayName);
        Assert.Equal("Current value", labels.Value);
        Assert.Null(labels.SubObjectType);
        Assert.Null(labels.DueDate);
    }
}
