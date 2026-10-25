using System.IO;
using Entraspect.App.Services;
using Entraspect.Core;

namespace Entraspect.App.Tests;

public sealed class SettingsStoreTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), $"entraspect-settings-{Guid.NewGuid()}");

    [Fact]
    public void Save_then_load_round_trips_settings()
    {
        var dir = TempDir();
        try
        {
            var store = new SettingsStore(dir);
            store.Save(new AppSettings
            {
                LastUsedProfileName = "p1",
                DisabledCheckIds = new() { "app-credential-expiry" },
                CheckOptions = new CheckOptions { ExpiryWarningDays = 21, ExpiryCriticalDays = 5 },
            });

            var loaded = store.Load(out var error);

            Assert.Null(error);
            Assert.Equal("p1", loaded.LastUsedProfileName);
            Assert.Equal(new[] { "app-credential-expiry" }, loaded.DisabledCheckIds);
            Assert.Equal(21, loaded.CheckOptions.ExpiryWarningDays);
            Assert.Equal(5, loaded.CheckOptions.ExpiryCriticalDays);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Load_missing_file_returns_defaults()
    {
        var dir = TempDir();
        try
        {
            var loaded = new SettingsStore(dir).Load(out var error);

            Assert.Null(error);
            Assert.Empty(loaded.DisabledCheckIds);
            Assert.Equal(30, loaded.CheckOptions.ExpiryWarningDays);
            Assert.Equal(7, loaded.CheckOptions.ExpiryCriticalDays);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Invalid_check_options_fall_back_to_defaults()
    {
        var dir = TempDir();
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(
                Path.Combine(dir, "settings.json"),
                "{\"checkOptions\":{\"expiryWarningDays\":-5,\"expiryCriticalDays\":100}}");

            var loaded = new SettingsStore(dir).Load(out var error);

            Assert.Null(error);
            Assert.Equal(30, loaded.CheckOptions.ExpiryWarningDays);
            Assert.Equal(7, loaded.CheckOptions.ExpiryCriticalDays);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Null_check_options_fall_back_to_defaults()
    {
        var dir = TempDir();
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "settings.json"), "{\"checkOptions\":null}");

            var loaded = new SettingsStore(dir).Load(out var error);

            Assert.Null(error);
            Assert.Equal(30, loaded.CheckOptions.ExpiryWarningDays);
            Assert.Equal(7, loaded.CheckOptions.ExpiryCriticalDays);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Valid_check_options_are_preserved()
    {
        var dir = TempDir();
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(
                Path.Combine(dir, "settings.json"),
                "{\"checkOptions\":{\"expiryWarningDays\":40,\"expiryCriticalDays\":10}}");

            var loaded = new SettingsStore(dir).Load(out var error);

            Assert.Null(error);
            Assert.Equal(40, loaded.CheckOptions.ExpiryWarningDays);
            Assert.Equal(10, loaded.CheckOptions.ExpiryCriticalDays);
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
            File.WriteAllText(Path.Combine(dir, "settings.json"), "{ not json");

            var loaded = new SettingsStore(dir).Load(out var error);

            Assert.NotNull(error);
            Assert.Empty(loaded.DisabledCheckIds);
            Assert.NotEmpty(Directory.GetFiles(dir, "settings.corrupt-*.json"));
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
            var store = new SettingsStore(dir);
            store.Save(new AppSettings { LastUsedProfileName = "p1" });

            var text = File.ReadAllText(Path.Combine(dir, "settings.json"));

            Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Time_zone_defaults_to_utc_and_round_trips()
    {
        var dir = TempDir();
        try
        {
            var store = new SettingsStore(dir);
            Assert.Equal(AppSettings.UtcTimeZone, store.Load(out _).TimeZone);

            store.Save(new AppSettings { TimeZone = "Europe/Berlin" });

            Assert.Equal("Europe/Berlin", store.Load(out var error).TimeZone);
            Assert.Null(error);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Theory]
    [InlineData("{\"UseLocalTime\":true}", "Local")]
    [InlineData("{\"useLocalTime\":false}", "UTC")]
    [InlineData("{\"UseLocalTime\":true,\"TimeZone\":\"Asia/Tokyo\"}", "Asia/Tokyo")]
    [InlineData("{\"TimeZone\":\"local\"}", "Local")]
    [InlineData("{\"TimeZone\":\"\"}", "UTC")]
    [InlineData("{\"TimeZone\":null}", "UTC")]
    public void Older_files_and_odd_values_load_a_time_zone(string json, string expected)
    {
        var dir = TempDir();
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "settings.json"), json);

            var loaded = new SettingsStore(dir).Load(out var error);

            Assert.Null(error);
            Assert.Equal(expected, loaded.TimeZone);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Use_local_time_is_no_longer_written()
    {
        var dir = TempDir();
        try
        {
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "settings.json");
            File.WriteAllText(path, "{\"UseLocalTime\":true}");
            var store = new SettingsStore(dir);

            store.Save(store.Load(out _));

            var text = File.ReadAllText(path);
            Assert.DoesNotContain("UseLocalTime", text);
            Assert.Contains("\"TimeZone\": \"Local\"", text);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
