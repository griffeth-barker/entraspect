using System.Collections.Generic;

namespace Entraspect.Core;

/// <summary>An enterprise app: a service principal in the tenant.</summary>
public sealed class EnterpriseApp
{
    public required string Id { get; init; }

    public required Guid AppId { get; init; }

    public required string DisplayName { get; init; }

    public required bool AccountEnabled { get; init; }

    /// <summary>The single sign-on mode set on the app's Single sign-on page, for example <c>saml</c>.</summary>
    public string? PreferredSingleSignOnMode { get; init; }

    /// <summary>Thumbprint of the active SAML token signing certificate, as Graph returns it.</summary>
    public string? PreferredTokenSigningKeyThumbprint { get; init; }

    /// <summary>The app's token signing certificates. Their matching verify credentials are not included.</summary>
    public required IReadOnlyList<SigningCertificate> SigningCertificates { get; init; }
}
