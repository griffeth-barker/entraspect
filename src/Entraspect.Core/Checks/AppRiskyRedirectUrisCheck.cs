using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Entraspect.Core.Checks;

/// <summary>
/// Looks only at the text of each redirect URI. It never resolves or contacts one: no DNS lookups and no requests,
/// so a URI on a domain that no longer exists isn't detected.
/// </summary>
public sealed class AppRiskyRedirectUrisCheck : ICheck
{
    private static readonly string[] LoopbackHosts = ["localhost", "127.0.0.1", "[::1]"];

    public string Id => "app-risky-redirect-uris";

    public string Title => "Risky redirect URIs";

    public string Description =>
        "Surfaces app registration redirect URIs that use a wildcard, unencrypted HTTP or this computer " +
        "(localhost) on the Web or single-page application platform, and URIs that aren't valid. Localhost on the " +
        "Mobile and desktop applications platform is normal and isn't reported. Only the URI text is checked; " +
        "entraspect never contacts it.";

    public CheckCategory Category => CheckCategory.AppRegistrations;

    public IReadOnlyList<string> RequiredPermissions => new[] { "Application.Read.All" };

    // Named like the platforms and redirect URIs on the app registration's Authentication page.
    public FindingLabels Labels { get; } = new()
    {
        ObjectDisplayName = "Display name",
        ObjectId = "Object ID",
        SubObjectDisplayName = "Redirect URI",
        SubObjectType = "Platform",
        DueDate = null,
    };

    public async Task<IReadOnlyList<Finding>> RunAsync(
        IDirectoryReader reader,
        CheckOptions options,
        CancellationToken cancellationToken = default)
    {
        var apps = await reader.GetAppRegistrationsAsync(cancellationToken).ConfigureAwait(false);
        var findings = new List<Finding>();

        foreach (var app in apps)
        {
            foreach (var redirectUri in app.RedirectUris)
            {
                if (Evaluate(redirectUri) is { } result)
                {
                    findings.Add(new Finding
                    {
                        CheckId = Id,
                        Severity = result.Severity,
                        ObjectType = "AppRegistration",
                        ObjectId = app.Id,
                        ObjectDisplayName = app.DisplayName,
                        Message = result.Message,
                        SubObjectType = PlatformName(redirectUri.Platform),
                        SubObjectDisplayName = redirectUri.Uri,
                    });
                }
            }
        }

        return findings;
    }

    /// <summary>
    /// The first rule the URI matches, so each URI gives at most one finding: a wildcard, then a URI that isn't a
    /// valid absolute URI, then HTTP to a host other than this computer, then this computer. The last two apply only
    /// to the Web and single-page application platforms.
    /// </summary>
    public static (Severity Severity, string Message)? Evaluate(RedirectUri redirectUri)
    {
        var text = redirectUri.Uri;

        if (text.Contains('*'))
        {
            return (Severity.Warning, "Wildcard redirect URI.");
        }

        if (!TryParseAbsolute(text, out var uri))
        {
            return (Severity.Info, "Redirect URI is not a valid URI.");
        }

        if (redirectUri.Platform == RedirectUriPlatform.PublicClient)
        {
            return null;
        }

        var isLoopback = IsLoopbackHost(uri.Host);

        if (!isLoopback && uri.Scheme == System.Uri.UriSchemeHttp)
        {
            return (Severity.Warning, "Redirect URI uses unencrypted HTTP.");
        }

        if (isLoopback)
        {
            return (Severity.Info, "Redirect URI points to this computer; usually left over from development.");
        }

        return null;
    }

    // On macOS and Linux a path such as "/callback" parses as an absolute file URI, so a file URI only counts when
    // the text names the file scheme itself.
    private static bool TryParseAbsolute(string text, out System.Uri uri)
    {
        if (!System.Uri.TryCreate(text, UriKind.Absolute, out uri!))
        {
            return false;
        }

        return !uri.IsFile || text.StartsWith("file:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLoopbackHost(string host)
    {
        foreach (var loopback in LoopbackHosts)
        {
            if (string.Equals(host, loopback, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The platform as the app registration's Authentication page names it.</summary>
    public static string PlatformName(RedirectUriPlatform platform) => platform switch
    {
        RedirectUriPlatform.Web => "Web",
        RedirectUriPlatform.Spa => "Single-page application",
        RedirectUriPlatform.PublicClient => "Mobile and desktop applications",
        _ => platform.ToString(),
    };
}
