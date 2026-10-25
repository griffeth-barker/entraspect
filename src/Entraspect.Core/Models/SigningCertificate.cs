namespace Entraspect.Core;

/// <summary>A token signing certificate on an enterprise app.</summary>
public sealed class SigningCertificate
{
    public required Guid KeyId { get; init; }

    /// <summary>The certificate thumbprint in uppercase hex, or empty if Graph didn't return one.</summary>
    public required string Thumbprint { get; init; }

    public required string DisplayName { get; init; }

    public required DateTimeOffset StartDateTime { get; init; }

    public DateTimeOffset? EndDateTime { get; init; }
}
