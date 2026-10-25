using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Entraspect.Core.Checks;

public sealed class AppNoOwnersCheck : ICheck
{
    /// <summary>
    /// Identifier URI of "P2P Server", the app registration Entra ID creates in the tenant for remote desktop to
    /// Entra-joined devices. It has no owners by design and admins aren't meant to manage it, so it is skipped.
    /// </summary>
    public const string P2PServerIdentifierUri = "urn:p2p_cert";

    public string Id => "app-no-owners";

    public string Title => "Apps with no owners";

    public string Description =>
        "Surfaces app registrations with no owners. No one is accountable for their credentials and permissions. " +
        "P2P Server, which Entra ID creates for remote desktop to Entra-joined devices, is skipped.";

    public CheckCategory Category => CheckCategory.AppRegistrations;

    public IReadOnlyList<string> RequiredPermissions => new[] { "Application.Read.All" };

    // Named like the app registration list in the Entra admin center.
    public FindingLabels Labels { get; } = new()
    {
        ObjectDisplayName = "Display name",
        ObjectId = "Object ID",
        SubObjectDisplayName = null,
        SubObjectType = null,
        DueDate = null,
    };

    public async Task<IReadOnlyList<Finding>> RunAsync(
        IDirectoryReader reader,
        CheckOptions options,
        CancellationToken cancellationToken = default)
    {
        var apps = await reader.GetAppRegistrationsAsync(cancellationToken).ConfigureAwait(false);

        return apps
            .Where(app => !app.HasOwners && !IsP2PServer(app))
            .Select(app => new Finding
            {
                CheckId = Id,
                Severity = Severity.Warning,
                ObjectType = "AppRegistration",
                ObjectId = app.Id,
                ObjectDisplayName = app.DisplayName,
                Message =
                    "App registration has no owners. No one is accountable for its credentials and permissions.",
            })
            .ToList();
    }

    private static bool IsP2PServer(AppRegistration app) =>
        app.IdentifierUris.Any(u => string.Equals(u, P2PServerIdentifierUri, StringComparison.OrdinalIgnoreCase));
}
