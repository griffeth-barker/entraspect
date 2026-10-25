namespace Entraspect.Core;

/// <summary>The platform a redirect URI is configured on, as on the app registration's Authentication page.</summary>
public enum RedirectUriPlatform
{
    Web,

    /// <summary>Single-page application.</summary>
    Spa,

    /// <summary>Mobile and desktop applications.</summary>
    PublicClient,
}

/// <summary>One redirect URI of an app registration, as text. It is never resolved or contacted.</summary>
public sealed record RedirectUri(RedirectUriPlatform Platform, string Uri);
