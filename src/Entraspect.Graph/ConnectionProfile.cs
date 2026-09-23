namespace Entraspect.Graph;

public enum AuthMode
{
    Interactive,
    AppOnly,
}

public sealed class ConnectionProfile
{
    public required string Name { get; init; }

    public required string TenantId { get; init; }

    public required string ClientId { get; init; }

    public required AuthMode AuthMode { get; init; }

    public string? CertificatePath { get; init; }

    public string? CertificateThumbprint { get; init; }
}
