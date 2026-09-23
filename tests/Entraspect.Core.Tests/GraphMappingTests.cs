using System;
using System.Linq;
using Entraspect.Core;
using Entraspect.Graph;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Xunit;

namespace Entraspect.Core.Tests;

public sealed class GraphMappingTests
{
    [Fact]
    public void MapApplication_maps_secrets_and_certificates()
    {
        var end = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var application = new Application
        {
            Id = "app-1",
            AppId = "11111111-1111-1111-1111-111111111111",
            DisplayName = "My App",
            PasswordCredentials = new System.Collections.Generic.List<PasswordCredential>
            {
                new PasswordCredential
                {
                    KeyId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    DisplayName = "secret-1",
                    StartDateTime = end.AddYears(-1),
                    EndDateTime = end,
                },
            },
            KeyCredentials = new System.Collections.Generic.List<KeyCredential>
            {
                new KeyCredential
                {
                    KeyId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    DisplayName = "cert-1",
                    StartDateTime = end.AddYears(-1),
                    EndDateTime = null,
                },
            },
        };

        var result = GraphDirectoryReader.MapApplication(application);

        Assert.Equal("app-1", result.Id);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), result.AppId);
        Assert.Equal("My App", result.DisplayName);
        Assert.Equal(2, result.Credentials.Count);

        var secret = result.Credentials.Single(c => c.Type == AppCredentialType.Secret);
        Assert.Equal("secret-1", secret.DisplayName);
        Assert.Equal(end, secret.EndDateTime);

        var cert = result.Credentials.Single(c => c.Type == AppCredentialType.Certificate);
        Assert.Equal("cert-1", cert.DisplayName);
        Assert.Null(cert.EndDateTime);
    }
}
