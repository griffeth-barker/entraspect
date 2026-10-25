using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Entraspect.Core.Checks;

public sealed class AppNoOwnersCheck : ICheck
{
    public string Id => "app-no-owners";

    public string Title => "Apps with no owners";

    public string Description =>
        "Surfaces app registrations with no owners. No one is accountable for their credentials and permissions.";

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
            .Where(app => !app.HasOwners)
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
}
