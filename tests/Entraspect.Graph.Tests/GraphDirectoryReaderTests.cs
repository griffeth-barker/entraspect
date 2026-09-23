using Entraspect.Core;
using Microsoft.Graph.Models;

namespace Entraspect.Graph.Tests;

// Mapping tests only: they build Graph SDK objects in memory and never create a client or credential.
public sealed class GraphDirectoryReaderTests
{
    private static readonly DateTimeOffset End = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MapApplication_maps_secrets_and_certificates()
    {
        var application = new Application
        {
            Id = "app-1",
            AppId = "11111111-1111-1111-1111-111111111111",
            DisplayName = "My App",
            PasswordCredentials =
            [
                new PasswordCredential
                {
                    KeyId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    DisplayName = "secret-1",
                    StartDateTime = End.AddYears(-1),
                    EndDateTime = End,
                },
            ],
            KeyCredentials =
            [
                new KeyCredential
                {
                    KeyId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    DisplayName = "cert-1",
                    StartDateTime = End.AddYears(-1),
                    EndDateTime = null,
                },
            ],
        };

        var result = GraphDirectoryReader.MapApplication(application);

        Assert.Equal("app-1", result.Id);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), result.AppId);
        Assert.Equal("My App", result.DisplayName);
        Assert.Equal(2, result.Credentials.Count);

        var secret = result.Credentials.Single(c => c.Type == AppCredentialType.Secret);
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), secret.KeyId);
        Assert.Equal("secret-1", secret.DisplayName);
        Assert.Equal(End.AddYears(-1), secret.StartDateTime);
        Assert.Equal(End, secret.EndDateTime);

        var cert = result.Credentials.Single(c => c.Type == AppCredentialType.Certificate);
        Assert.Equal("cert-1", cert.DisplayName);
        Assert.Null(cert.EndDateTime);
    }

    [Fact]
    public void MapApplication_with_null_collections_has_no_credentials()
    {
        var application = new Application
        {
            Id = "app-1",
            AppId = "11111111-1111-1111-1111-111111111111",
            DisplayName = "My App",
            PasswordCredentials = null,
            KeyCredentials = null,
        };

        var result = GraphDirectoryReader.MapApplication(application);

        Assert.Empty(result.Credentials);
    }

    [Fact]
    public void MapApplication_with_null_fields_uses_empty_values()
    {
        var result = GraphDirectoryReader.MapApplication(new Application());

        Assert.Equal(string.Empty, result.Id);
        Assert.Equal(Guid.Empty, result.AppId);
        Assert.Equal(string.Empty, result.DisplayName);
    }

    [Fact]
    public void MapApplication_with_malformed_app_id_uses_empty_guid()
    {
        var result = GraphDirectoryReader.MapApplication(new Application { AppId = "not-a-guid" });

        Assert.Equal(Guid.Empty, result.AppId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MapApplication_credential_without_display_name_falls_back_to_key_id(string? displayName)
    {
        var keyId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var application = new Application
        {
            PasswordCredentials = [new PasswordCredential { KeyId = keyId, DisplayName = displayName, EndDateTime = End }],
            KeyCredentials = [new KeyCredential { KeyId = keyId, DisplayName = displayName, EndDateTime = End }],
        };

        var result = GraphDirectoryReader.MapApplication(application);

        Assert.All(result.Credentials, c => Assert.Equal(keyId.ToString(), c.DisplayName));
    }
}
