using Entraspect.Core;
using Microsoft.Graph.Models;

namespace Entraspect.Graph.Tests;

// Mapping tests only: they build Graph SDK objects in memory and never create a client or credential.
public sealed class EnterpriseAppMappingTests
{
    private static readonly DateTimeOffset End = new(2027, 3, 1, 0, 0, 0, TimeSpan.Zero);

    // A made-up SHA-1 sized identifier; its hex is the thumbprint.
    private static readonly byte[] ThumbprintBytes =
        [0x0A, 0x1B, 0x2C, 0x3D, 0x4E, 0x5F, 0x60, 0x71, 0x82, 0x93, 0xA4, 0xB5, 0xC6, 0xD7, 0xE8, 0xF9, 0x01, 0x23, 0x45, 0x67];

    private const string Thumbprint = "0A1B2C3D4E5F60718293A4B5C6D7E8F901234567";

    private static KeyCredential Key(string usage, Guid keyId, byte[]? identifier = null, string type = "AsymmetricX509Cert") =>
        new()
        {
            KeyId = keyId,
            Type = type,
            Usage = usage,
            CustomKeyIdentifier = identifier ?? ThumbprintBytes,
            DisplayName = "CN=Microsoft Azure Federated SSO Certificate",
            StartDateTime = End.AddYears(-3),
            EndDateTime = End,
        };

    [Fact]
    public void MapServicePrincipal_keeps_the_sign_credential_of_a_sign_and_verify_pair()
    {
        var signId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var servicePrincipal = new ServicePrincipal
        {
            Id = "sp-1",
            AppId = "11111111-1111-1111-1111-111111111111",
            DisplayName = "Contoso SAML",
            AccountEnabled = true,
            PreferredSingleSignOnMode = "saml",
            PreferredTokenSigningKeyThumbprint = Thumbprint,
            KeyCredentials =
            [
                Key("Verify", Guid.Parse("55555555-5555-5555-5555-555555555555")),
                Key("Sign", signId),
            ],
        };

        var app = GraphDirectoryReader.MapServicePrincipal(servicePrincipal);

        Assert.Equal("sp-1", app.Id);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), app.AppId);
        Assert.Equal("Contoso SAML", app.DisplayName);
        Assert.True(app.AccountEnabled);
        Assert.Equal("saml", app.PreferredSingleSignOnMode);
        Assert.Equal(Thumbprint, app.PreferredTokenSigningKeyThumbprint);

        var certificate = Assert.Single(app.SigningCertificates);
        Assert.Equal(signId, certificate.KeyId);
        Assert.Equal(Thumbprint, certificate.Thumbprint);
        Assert.Equal("CN=Microsoft Azure Federated SSO Certificate", certificate.DisplayName);
        Assert.Equal(End.AddYears(-3), certificate.StartDateTime);
        Assert.Equal(End, certificate.EndDateTime);
    }

    [Fact]
    public void MapServicePrincipal_keeps_every_signing_certificate()
    {
        var servicePrincipal = new ServicePrincipal
        {
            Id = "sp-1",
            PreferredSingleSignOnMode = "saml",
            KeyCredentials =
            [
                Key("Sign", Guid.NewGuid()),
                Key("Verify", Guid.NewGuid()),
                WithKey(Key("Sign", Guid.NewGuid(), Sha256Sized)),
                WithKey(Key("Verify", Guid.NewGuid(), Sha256Sized)),
            ],
        };

        var app = GraphDirectoryReader.MapServicePrincipal(servicePrincipal);

        Assert.Equal(new[] { Thumbprint, AbcSha1 }, app.SigningCertificates.Select(c => c.Thumbprint));
    }

    [Theory]
    [InlineData("AsymmetricX509Cert", "Sign", true)]
    [InlineData("asymmetricx509cert", "sign", true)]
    [InlineData("AsymmetricX509Cert", "Verify", false)]
    [InlineData("AsymmetricX509Cert", "Encrypt", false)]
    [InlineData("Symmetric", "Sign", false)]
    [InlineData(null, "Sign", false)]
    [InlineData("AsymmetricX509Cert", null, false)]
    public void IsSigningCertificate_needs_an_x509_sign_credential(string? type, string? usage, bool expected)
    {
        var credential = new KeyCredential { Type = type, Usage = usage };

        Assert.Equal(expected, GraphDirectoryReader.IsSigningCertificate(credential));
    }

    [Fact]
    public void MapSigningCertificate_with_missing_values_uses_empty_values()
    {
        var certificate = GraphDirectoryReader.MapSigningCertificate(new KeyCredential());

        Assert.Equal(Guid.Empty, certificate.KeyId);
        Assert.Equal(string.Empty, certificate.Thumbprint);
        Assert.Equal(string.Empty, certificate.DisplayName);
        Assert.Equal(default, certificate.StartDateTime);
        Assert.Null(certificate.EndDateTime);
    }

    [Fact]
    public void MapServicePrincipal_with_null_fields_uses_empty_values()
    {
        var app = GraphDirectoryReader.MapServicePrincipal(new ServicePrincipal());

        Assert.Equal(string.Empty, app.Id);
        Assert.Equal(Guid.Empty, app.AppId);
        Assert.Equal(string.Empty, app.DisplayName);
        Assert.True(app.AccountEnabled);
        Assert.Null(app.PreferredSingleSignOnMode);
        Assert.Null(app.PreferredTokenSigningKeyThumbprint);
        Assert.Empty(app.SigningCertificates);
    }

    [Fact]
    public void MapServicePrincipal_maps_a_disabled_non_saml_app()
    {
        var app = GraphDirectoryReader.MapServicePrincipal(new ServicePrincipal
        {
            AccountEnabled = false,
            PreferredSingleSignOnMode = "oidc",
        });

        Assert.False(app.AccountEnabled);
        Assert.Equal("oidc", app.PreferredSingleSignOnMode);
    }

    // SHA-1("abc"), a published test vector, stands in for a certificate's thumbprint.
    private static readonly byte[] Abc = "abc"u8.ToArray();

    private const string AbcSha1 = "A9993E364706816ABA3E25717850C26C9CD0D89D";

    // What Entra sets as customKeyIdentifier on SAML signing certificates: 32 bytes, a SHA-256 hash.
    private static readonly byte[] Sha256Sized = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();

    [Fact]
    public void Thumbprint_is_the_sha1_of_the_certificate_key()
    {
        var credential = new KeyCredential { Key = Abc, CustomKeyIdentifier = Sha256Sized };

        Assert.Equal(AbcSha1, GraphDirectoryReader.Thumbprint(credential));
    }

    [Fact]
    public void Thumbprint_without_key_ignores_a_sha256_sized_identifier()
    {
        var credential = new KeyCredential { CustomKeyIdentifier = Sha256Sized };

        Assert.Equal(string.Empty, GraphDirectoryReader.Thumbprint(credential));
    }

    [Fact]
    public void Thumbprint_without_key_uses_a_sha1_sized_identifier()
    {
        var credential = new KeyCredential { CustomKeyIdentifier = ThumbprintBytes };

        Assert.Equal(Thumbprint, GraphDirectoryReader.Thumbprint(credential));
    }

    [Fact]
    public void MapServicePrincipal_matches_the_preferred_thumbprint_from_the_key_like_entra_returns_it()
    {
        // Shaped like a real SAML app read with GET /servicePrincipals/{id}?$select=keyCredentials: a Sign and Verify
        // pair sharing a SHA-256 customKeyIdentifier, with the certificate in key.
        var servicePrincipal = new ServicePrincipal
        {
            Id = "sp-1",
            PreferredSingleSignOnMode = "saml",
            PreferredTokenSigningKeyThumbprint = AbcSha1,
            KeyCredentials =
            [
                WithKey(Key("Verify", Guid.NewGuid(), Sha256Sized)),
                WithKey(Key("Sign", Guid.NewGuid(), Sha256Sized)),
            ],
        };

        var app = GraphDirectoryReader.MapServicePrincipal(servicePrincipal);

        Assert.Equal(AbcSha1, Assert.Single(app.SigningCertificates).Thumbprint);
        Assert.Equal(app.PreferredTokenSigningKeyThumbprint, app.SigningCertificates[0].Thumbprint);
    }

    private static KeyCredential WithKey(KeyCredential credential)
    {
        credential.Key = Abc;
        return credential;
    }
}
