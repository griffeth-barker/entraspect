using Entraspect.Core;
using Microsoft.Graph.Models;

namespace Entraspect.Graph.Tests;

// Mapping tests only: they build Graph SDK objects in memory and never create a client or credential.
public sealed class TenantLicenceMappingTests
{
    private static readonly Guid P1 = Guid.Parse("41781fb2-bc02-4b7c-bd55-b576c07bb09d");
    private static readonly Guid P2 = Guid.Parse("eec0eb4f-6444-4f95-aba0-50c24d67f998");
    private static readonly Guid Exchange = Guid.Parse("113feb6c-3fe4-4440-bddc-54d774bf0318");

    private static SubscribedSku Sku(string? capabilityStatus, params (Guid Id, string? Status)[] plans) => new()
    {
        CapabilityStatus = capabilityStatus,
        ServicePlans = plans
            .Select(p => new ServicePlanInfo { ServicePlanId = p.Id, ProvisioningStatus = p.Status })
            .ToList(),
    };

    private static TenantLicence Map(params SubscribedSku[] skus) => TenantLicenceReader.MapTenantLicence(skus);

    [Fact]
    public void Standalone_p1_is_p1()
    {
        Assert.Equal(TenantLicence.EntraIdP1, Map(Sku("Enabled", (P1, "Success"))));
    }

    [Fact]
    public void P2_wins_over_p1()
    {
        Assert.Equal(
            TenantLicence.EntraIdP2,
            Map(Sku("Enabled", (P1, "Success")), Sku("Enabled", (P2, "Success"), (P1, "Success"))));
    }

    [Fact]
    public void Bundle_with_the_p1_plan_is_p1()
    {
        // For example Microsoft 365 E3, which includes AAD_PREMIUM alongside other plans.
        Assert.Equal(TenantLicence.EntraIdP1, Map(Sku("Enabled", (Exchange, "Success"), (P1, "Success"))));
    }

    [Fact]
    public void Subscription_in_its_grace_period_still_counts()
    {
        Assert.Equal(TenantLicence.EntraIdP2, Map(Sku("Warning", (P2, "Success"))));
    }

    [Theory]
    [InlineData("Suspended")]
    [InlineData("Deleted")]
    [InlineData("LockedOut")]
    [InlineData(null)]
    public void Inactive_subscription_does_not_count(string? capabilityStatus)
    {
        Assert.Equal(TenantLicence.Free, Map(Sku(capabilityStatus, (P1, "Success"))));
    }

    [Fact]
    public void Disabled_plan_does_not_count()
    {
        Assert.Equal(TenantLicence.EntraIdP1, Map(Sku("Enabled", (P2, "Disabled"), (P1, "Success"))));
    }

    [Fact]
    public void No_premium_plan_is_free()
    {
        Assert.Equal(TenantLicence.Free, Map(Sku("Enabled", (Exchange, "Success"))));
        Assert.Equal(TenantLicence.Free, Map());
    }

    [Fact]
    public void Missing_values_do_not_throw()
    {
        Assert.Equal(
            TenantLicence.Free,
            Map(new SubscribedSku(), new SubscribedSku { CapabilityStatus = "Enabled" },
                new SubscribedSku { CapabilityStatus = "Enabled", ServicePlans = [new ServicePlanInfo()] }));
    }

    [Fact]
    public void Missing_provisioning_status_still_counts()
    {
        Assert.Equal(TenantLicence.EntraIdP1, Map(Sku("Enabled", (P1, null))));
    }
}
