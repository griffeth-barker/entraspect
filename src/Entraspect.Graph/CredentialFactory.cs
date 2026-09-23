using System.Security.Cryptography.X509Certificates;
using Azure.Core;
using Azure.Identity;
using Microsoft.Graph;

namespace Entraspect.Graph;

public static class CredentialFactory
{
    public const string GraphScope = "https://graph.microsoft.com/.default";

    public static TokenCredential Create(ConnectionProfile profile, string? certificatePassword = null)
    {
        if (profile.AuthMode == AuthMode.Interactive)
        {
            return new InteractiveBrowserCredential(
                profile.TenantId,
                profile.ClientId,
                new InteractiveBrowserCredentialOptions
                {
                    RedirectUri = new Uri("http://localhost"),
                });
        }

        var certificate = LoadCertificate(profile, certificatePassword);
        return new ClientCertificateCredential(profile.TenantId, profile.ClientId, certificate);
    }

    public static GraphServiceClient CreateGraphClient(TokenCredential credential) =>
        new(credential, new[] { GraphScope });

    private static X509Certificate2 LoadCertificate(ConnectionProfile profile, string? password)
    {
        if (!string.IsNullOrWhiteSpace(profile.CertificateThumbprint))
        {
            return LoadFromStore(profile.CertificateThumbprint);
        }

        if (!string.IsNullOrWhiteSpace(profile.CertificatePath))
        {
            var bytes = File.ReadAllBytes(profile.CertificatePath);
            return X509CertificateLoader.LoadPkcs12(
                bytes,
                password,
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet);
        }

        throw new InvalidOperationException(
            "App-only authentication requires a certificate path or thumbprint.");
    }

    private static X509Certificate2 LoadFromStore(string thumbprint)
    {
        foreach (var location in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
        {
            using var store = new X509Store(StoreName.My, location);
            store.Open(OpenFlags.ReadOnly);
            var matches = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);
            if (matches.Count > 0)
            {
                return matches[0];
            }
        }

        throw new InvalidOperationException(
            $"No certificate with thumbprint '{thumbprint}' was found in the certificate store.");
    }
}
