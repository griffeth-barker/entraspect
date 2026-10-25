using Entraspect.Core;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace Entraspect.Graph;

/// <summary>
/// Reads the tenant's Entra ID licence at connect from <c>GET /subscribedSkus</c>, which needs
/// <c>LicenseAssignment.Read.All</c>. Without that permission, or if the read fails, the licence is
/// <see cref="TenantLicence.Unknown"/> and every check runs as before.
/// </summary>
internal static class TenantLicenceReader
{
    /// <summary>The <c>AAD_PREMIUM</c> service plan (Microsoft Entra ID P1), in standalone and bundled SKUs.</summary>
    public static readonly Guid EntraIdP1ServicePlanId = Guid.Parse("41781fb2-bc02-4b7c-bd55-b576c07bb09d");

    /// <summary>The <c>AAD_PREMIUM_P2</c> service plan (Microsoft Entra ID P2), in standalone and bundled SKUs.</summary>
    public static readonly Guid EntraIdP2ServicePlanId = Guid.Parse("eec0eb4f-6444-4f95-aba0-50c24d67f998");

    public static async Task<TenantLicence> ReadAsync(
        GraphServiceClient client,
        IReadOnlyCollection<string> grantedPermissions,
        CancellationToken cancellationToken)
    {
        if (!grantedPermissions.Contains(CheckCatalog.LicencePermission))
        {
            return TenantLicence.Unknown;
        }

        try
        {
            var skus = new List<SubscribedSku>();
            SubscribedSkuCollectionResponse? response = await client.SubscribedSkus.GetAsync(config =>
            {
                config.QueryParameters.Select = ["capabilityStatus", "servicePlans"];
            }, cancellationToken).ConfigureAwait(false);

            while (response is not null)
            {
                if (response.Value is not null)
                {
                    skus.AddRange(response.Value);
                }

                response = string.IsNullOrEmpty(response.OdataNextLink)
                    ? null
                    : await client.SubscribedSkus.WithUrl(response.OdataNextLink)
                        .GetAsync(_ => { }, cancellationToken).ConfigureAwait(false);
            }

            return MapTenantLicence(skus);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Any failure here (Graph error, network, timeout) only means the licence is unknown; never fail connect.
            return TenantLicence.Unknown;
        }
    }

    /// <summary>
    /// P2 when a subscription that is <c>Enabled</c> or <c>Warning</c> (expired but in its grace period, so features
    /// still work) has the P2 service plan; otherwise P1 with the P1 service plan; otherwise free. Matches service
    /// plans, not SKUs, because bundles such as Microsoft 365 E3 and E5 include them. A plan whose provisioning status
    /// is <c>Disabled</c> doesn't count.
    /// </summary>
    public static TenantLicence MapTenantLicence(IEnumerable<SubscribedSku> skus)
    {
        var plans = skus
            .Where(IsActive)
            .SelectMany(s => s.ServicePlans ?? [])
            .Where(p => !string.Equals(p.ProvisioningStatus, "Disabled", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.ServicePlanId)
            .ToHashSet();

        if (plans.Contains(EntraIdP2ServicePlanId))
        {
            return TenantLicence.EntraIdP2;
        }

        return plans.Contains(EntraIdP1ServicePlanId) ? TenantLicence.EntraIdP1 : TenantLicence.Free;
    }

    private static bool IsActive(SubscribedSku sku) =>
        string.Equals(sku.CapabilityStatus, "Enabled", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(sku.CapabilityStatus, "Warning", StringComparison.OrdinalIgnoreCase);
}
