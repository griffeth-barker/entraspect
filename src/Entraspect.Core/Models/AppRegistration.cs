using System.Collections.Generic;

namespace Entraspect.Core;

public sealed class AppRegistration
{
    public required string Id { get; init; }

    public required Guid AppId { get; init; }

    public required string DisplayName { get; init; }

    public required IReadOnlyList<AppCredential> Credentials { get; init; }

    /// <summary>Who can sign in to the app, for example <c>AzureADMyOrg</c>, as Graph returns it.</summary>
    public string? SignInAudience { get; init; }

    /// <summary>The app's identifier URIs (Application ID URIs), for example <c>api://contoso</c>.</summary>
    public IReadOnlyList<string> IdentifierUris { get; init; } = [];

    /// <summary>The redirect URIs of the Web, single-page application and public client platforms.</summary>
    public IReadOnlyList<RedirectUri> RedirectUris { get; init; } = [];

    /// <summary>
    /// Whether the app registration has at least one owner. Defaults to true, so an app built without owner data
    /// isn't reported as ownerless.
    /// </summary>
    public bool HasOwners { get; init; } = true;
}
