using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Azure.Core;
using Azure.Identity;

namespace Entraspect.Graph;

internal static class CredentialFactory
{
    public const string GraphScope = "https://graph.microsoft.com/.default";

    public static TokenCredential Create(ConnectionProfile profile, string? certificatePassword = null)
    {
        var certificate = LoadCertificate(profile, certificatePassword);
        return new ClientCertificateCredential(profile.TenantId, profile.ClientId, certificate);
    }

    /// <summary>
    /// Keeps only hex digits, so thumbprints pasted from Windows certmgr (leading U+200E,
    /// spaces) or with colons still match.
    /// </summary>
    internal static string NormalizeThumbprint(string thumbprint) =>
        new string(thumbprint.Where(char.IsAsciiHexDigit).ToArray()).ToUpperInvariant();

    private static X509Certificate2 LoadCertificate(ConnectionProfile profile, string? password)
    {
        if (!string.IsNullOrWhiteSpace(profile.CertificateThumbprint))
        {
            return LoadFromStore(NormalizeThumbprint(profile.CertificateThumbprint));
        }

        if (!string.IsNullOrWhiteSpace(profile.CertificatePath))
        {
            return LoadFromFile(profile.CertificatePath, password);
        }

        throw new InvalidOperationException(
            "App-only authentication requires a certificate path or thumbprint.");
    }

    private static X509Certificate2 LoadFromFile(string path, string? password)
    {
        // macOS does not support EphemeralKeySet; elsewhere it keeps the private key off disk.
        var flags = OperatingSystem.IsMacOS()
            ? X509KeyStorageFlags.DefaultKeySet
            : X509KeyStorageFlags.EphemeralKeySet;

        // The loader reports a missing file as a CryptographicException, which reads like a bad password.
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"The certificate file '{path}' was not found.");
        }

        X509Certificate2 certificate;
        try
        {
            certificate = X509CertificateLoader.LoadPkcs12FromFile(path, password, flags);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException(
                $"Could not open the certificate file '{path}'. Check that it is a .pfx file and that the password is correct.",
                ex);
        }

        if (!certificate.HasPrivateKey)
        {
            certificate.Dispose();
            throw new InvalidOperationException(
                $"The certificate file '{path}' does not contain a private key.");
        }

        return certificate;
    }

    private static X509Certificate2 LoadFromStore(string thumbprint)
    {
        // On Linux, .NET only supports the Root and CertificateAuthority stores for LocalMachine.
        StoreLocation[] locations = OperatingSystem.IsLinux()
            ? [StoreLocation.CurrentUser]
            : [StoreLocation.CurrentUser, StoreLocation.LocalMachine];

        var foundWithoutPrivateKey = false;
        foreach (var location in locations)
        {
            using var store = new X509Store(StoreName.My, location);
            store.Open(OpenFlags.ReadOnly);
            var matches = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);
            var match = matches.FirstOrDefault(c => c.HasPrivateKey);
            if (match is not null)
            {
                return match;
            }

            foundWithoutPrivateKey |= matches.Count > 0;
        }

        throw new InvalidOperationException(foundWithoutPrivateKey
            ? $"The certificate with thumbprint '{thumbprint}' was found, but it has no private key."
            : $"No certificate with thumbprint '{thumbprint}' was found in the certificate store.");
    }
}
