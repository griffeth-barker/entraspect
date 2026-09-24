using System.IO;
using Entraspect.App.Services;
using Entraspect.Graph;

namespace Entraspect.App.Tests;

public sealed class ProfileStoreTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), $"entraspect-profiles-{Guid.NewGuid()}");

    [Fact]
    public void Save_then_load_round_trips_profiles()
    {
        var dir = TempDir();
        try
        {
            var store = new ProfileStore(dir);
            store.Save(new[]
            {
                new ConnectionProfile { Name = "p1", TenantId = "t1", ClientId = "c1", CertificateThumbprint = "ABC" },
                new ConnectionProfile { Name = "p2", TenantId = "t2", ClientId = "c2", CertificatePath = "/x.pfx" },
            });

            var loaded = store.Load(out var error);

            Assert.Null(error);
            Assert.Equal(2, loaded.Count);
            Assert.Equal("p1", loaded[0].Name);
            Assert.Equal("ABC", loaded[0].CertificateThumbprint);
            Assert.Equal("/x.pfx", loaded[1].CertificatePath);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Load_missing_file_returns_empty()
    {
        var dir = TempDir();
        try
        {
            var loaded = new ProfileStore(dir).Load(out var error);

            Assert.Null(error);
            Assert.Empty(loaded);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Old_profile_with_auth_mode_still_loads()
    {
        var dir = TempDir();
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(
                Path.Combine(dir, "profiles.json"),
                "[{\"name\":\"legacy\",\"tenantId\":\"t\",\"clientId\":\"c\",\"authMode\":\"Interactive\"}]");

            var loaded = new ProfileStore(dir).Load(out var error);

            Assert.Null(error);
            Assert.Single(loaded);
            Assert.Equal("legacy", loaded[0].Name);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Corrupt_file_is_backed_up_and_error_returned()
    {
        var dir = TempDir();
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "profiles.json"), "{ this is not valid json");

            var loaded = new ProfileStore(dir).Load(out var error);

            Assert.NotNull(error);
            Assert.Empty(loaded);
            Assert.NotEmpty(Directory.GetFiles(dir, "profiles.corrupt-*.json"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Saved_file_never_contains_a_password()
    {
        var dir = TempDir();
        try
        {
            var store = new ProfileStore(dir);
            store.Save(new[]
            {
                new ConnectionProfile { Name = "p1", TenantId = "t1", ClientId = "c1", CertificateThumbprint = "ABC" },
            });

            var text = File.ReadAllText(Path.Combine(dir, "profiles.json"));

            Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
