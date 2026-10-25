using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Entraspect.Core.Checks;

public sealed class HighPrivilegeAppPermissionsCheck : ICheck
{
    /// <summary>The tenant that owns Microsoft's own apps.</summary>
    public static readonly Guid MicrosoftTenantId = Guid.Parse("f8cdef31-a31e-4b4a-93e4-5f571e91255a");

    public string Id => "sp-high-privilege-app-permissions";

    public string Title => "High-privilege apps";

    public string Description =>
        "Surfaces enterprise apps holding Microsoft Graph application permissions that can take over the tenant " +
        "(Critical) or reach data across the whole tenant (Warning). Findings on disabled apps are Info. " +
        "Microsoft's own apps are skipped. Delegated permissions and permissions on other APIs aren't checked.";

    public CheckCategory Category => CheckCategory.EnterpriseApps;

    public IReadOnlyList<string> RequiredPermissions => new[] { "Application.Read.All" };

    // Named like the enterprise app and its Permissions page in the Entra admin center. Every finding is about an
    // application permission, so the type column is hidden; the date column is when the permission was granted.
    public FindingLabels Labels { get; } = new()
    {
        ObjectDisplayName = "Display name",
        ObjectId = "Object ID",
        SubObjectDisplayName = "Permission",
        SubObjectType = null,
        DueDate = "Granted",
    };

    public async Task<IReadOnlyList<Finding>> RunAsync(
        IDirectoryReader reader,
        CheckOptions options,
        CancellationToken cancellationToken = default)
    {
        var grants = await reader.GetGraphApplicationPermissionGrantsAsync(cancellationToken).ConfigureAwait(false);
        var apps = await reader.GetEnterpriseAppsAsync(cancellationToken).ConfigureAwait(false);
        var appsById = apps
            .GroupBy(a => a.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var findings = new List<Finding>();

        foreach (var grant in grants)
        {
            appsById.TryGetValue(grant.ServicePrincipalId, out var app);

            if (app?.AppOwnerOrganizationId == MicrosoftTenantId ||
                HighPrivilegePermissions.SeverityOf(grant.PermissionName) is not { } severity)
            {
                continue;
            }

            // A disabled app can't get tokens, so nothing is urgent; the finding is kept because re-enabling the app
            // restores the permission. An app missing from the list counts as enabled so it keeps its severity.
            var isDisabled = app is { AccountEnabled: false };
            var message = $"Has Microsoft Graph application permission {grant.PermissionName}.";

            findings.Add(new Finding
            {
                CheckId = Id,
                Severity = isDisabled ? Severity.Info : severity,
                ObjectType = "EnterpriseApp",
                ObjectId = grant.ServicePrincipalId,
                ObjectDisplayName = grant.ServicePrincipalDisplayName,
                // The grant date is in its own column, shown in the time zone chosen in Settings > Preferences.
                Message = isDisabled ? "App is disabled. " + message : message,
                DueDate = grant.CreatedDateTime,
                SubObjectType = "Application permission",
                SubObjectDisplayName = grant.PermissionName,
            });
        }

        return findings;
    }
}
