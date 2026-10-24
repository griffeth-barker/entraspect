using System.IO;
using System.Text.Json;
using Entraspect.Core;

namespace Entraspect.App.Services;

/// <summary>
/// Saves application settings as settings.json next to profiles.json. CheckOptions values are validated on load and
/// fall back to their defaults if they break <c>0 &lt;= ExpiryCriticalDays &lt;= ExpiryWarningDays</c>.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _directory;
    private readonly string _filePath;

    public SettingsStore() : this(DefaultDirectory())
    {
    }

    /// <summary>Uses <paramref name="directory"/> for storage instead of the application data folder.</summary>
    public SettingsStore(string directory)
    {
        _directory = directory;
        _filePath = Path.Combine(_directory, "settings.json");
    }

    public AppSettings Load(out string? error)
    {
        error = null;
        if (!File.Exists(_filePath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var raw = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
            return new AppSettings
            {
                DisabledCheckIds = raw.DisabledCheckIds ?? new List<string>(),
                LastUsedProfileName = raw.LastUsedProfileName,
                CheckOptions = CheckOptions.Normalize(raw.CheckOptions),
            };
        }
        catch (JsonException)
        {
            var backupPath = StoreBackup.Backup(_filePath, _directory, out var failure);
            error = backupPath is not null
                ? $"Settings could not be read. The file was backed up to '{backupPath}'."
                : $"Settings in '{_filePath}' could not be read, and a backup could not be made: {failure}";
            return new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = $"Settings could not be read: {ex.Message}";
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(_directory);
        var json = JsonSerializer.Serialize(settings, Options);
        File.WriteAllText(_filePath, json);
    }

    private static string DefaultDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "entraspect");
}
