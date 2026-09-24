namespace Entraspect.Graph;

public sealed class ConnectionProfile
{
    public required string Name { get; init; }

    public required string TenantId { get; init; }

    public required string ClientId { get; init; }

    /// <summary>Path to a .pfx file. The password is supplied at connect time and never stored.</summary>
    public string? CertificatePath { get; init; }

    public string? CertificateThumbprint { get; init; }
}
