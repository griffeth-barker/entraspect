using System.IO;
using Entraspect.App.Services;
using Entraspect.App.ViewModels;
using Entraspect.Graph;

namespace Entraspect.App.Tests;

public sealed class ConnectionViewModelTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), $"entraspect-connection-{Guid.NewGuid()}");

    [Fact]
    public void Starts_with_the_last_used_profile_selected_and_loaded()
    {
        var dir = TempDir();
        try
        {
            new ProfileStore(dir).Save(new[]
            {
                new ConnectionProfile { Name = "p1", TenantId = "t1", ClientId = "c1", CertificateThumbprint = "AB" },
                new ConnectionProfile { Name = "p2", TenantId = "t2", ClientId = "c2", CertificatePath = "/x.pfx" },
            });

            var vm = new ConnectionViewModel(new ProfileStore(dir), "p2");

            Assert.Equal("p2", vm.SelectedProfile?.Name);
            Assert.Equal("t2", vm.TenantId);
            Assert.Equal("c2", vm.ClientId);
            Assert.Equal("/x.pfx", vm.CertificatePath);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void A_profile_without_a_certificate_cannot_connect()
    {
        var vm = new ConnectionViewModel(new ProfileStore(TempDir())) { Name = "old", TenantId = "t", ClientId = "c" };

        Assert.True(vm.NeedsCertificate);
        Assert.Null(vm.TryGetProfileToConnect(out var error));
        Assert.Equal("Add a certificate to use this profile.", error);
    }

    [Fact]
    public void Tenant_and_client_ids_are_required_to_connect()
    {
        var vm = new ConnectionViewModel(new ProfileStore(TempDir())) { CertificateThumbprint = "AB" };

        Assert.Null(vm.TryGetProfileToConnect(out var error));
        Assert.Contains("Tenant ID and Client ID are required", error);
    }

    [Theory]
    [InlineData("/x.pfx", null, true)]
    [InlineData("/x.pfx", "AB", false)]
    [InlineData(null, "AB", false)]
    public void Only_a_pfx_without_a_thumbprint_needs_a_password(string? path, string? thumbprint, bool expected)
    {
        var profile = new ConnectionProfile
        {
            Name = "p", TenantId = "t", ClientId = "c", CertificatePath = path, CertificateThumbprint = thumbprint,
        };

        Assert.Equal(expected, ConnectionViewModel.NeedsPassword(profile));
    }
}
