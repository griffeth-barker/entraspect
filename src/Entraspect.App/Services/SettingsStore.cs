using System.IO;
using System.Text.Json;
using Entraspect.Core;

namespace Entraspect.App.Services;

/// <summary>
/// Saves application settings as settings.json next to profiles.json. CheckOptions values are validated on load with
/// <see cref="CheckOptions.Normalize"/>: values that break their rule fall back to their defaults. A file written before
/// the time zone choice (<c>"UseLocalTime": true</c> and no <c>TimeZone</c>) loads as local time. A missing or unknown
/// theme loads as <see cref="AppTheme.System"/>.
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
            var raw = JsonSerializer.Deserialize<SettingsFile>(json, Options) ?? new SettingsFile();
            return new AppSettings
            {
                DisabledCheckIds = raw.DisabledCheckIds ?? new List<string>(),
                LastUsedProfileName = raw.LastUsedProfileName,
                TimeZone = TimeZoneFrom(raw),
                Theme = ThemeFrom(raw.Theme) ?? AppTheme.System,
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

    private static string TimeZoneFrom(SettingsFile raw)
    {
        var zone = raw.TimeZone?.Trim();
        if (string.IsNullOrEmpty(zone))
        {
            return raw.UseLocalTime == true ? AppSettings.LocalTimeZone : AppSettings.UtcTimeZone;
        }

        if (string.Equals(zone, AppSettings.UtcTimeZone, StringComparison.OrdinalIgnoreCase))
        {
            return AppSettings.UtcTimeZone;
        }

        return string.Equals(zone, AppSettings.LocalTimeZone, StringComparison.OrdinalIgnoreCase)
            ? AppSettings.LocalTimeZone
            : zone;
    }

    /// <summary>The theme named by <paramref name="value"/>, ignoring case, or null if it isn't one.</summary>
    private static AppTheme? ThemeFrom(JsonElement? value)
    {
        if (value is not { ValueKind: JsonValueKind.String } element)
        {
            return null;
        }

        var name = element.GetString()?.Trim();
        return Enum.GetValues<AppTheme>()
            .Select(t => (AppTheme?)t)
            .FirstOrDefault(t => string.Equals(t.ToString(), name, StringComparison.OrdinalIgnoreCase));
    }

    private static string DefaultDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "entraspect");

    /// <summary>settings.json as written, including fields older versions wrote. Only <see cref="AppSettings"/> is saved.</summary>
    private sealed class SettingsFile
    {
        public List<string>? DisabledCheckIds { get; init; }

        public CheckOptions? CheckOptions { get; init; }

        public string? LastUsedProfileName { get; init; }

        public string? TimeZone { get; init; }

        /// <summary>Read as JSON so an unknown or mistyped value falls back to the default instead of failing.</summary>
        public JsonElement? Theme { get; init; }

        /// <summary>Written before the time zone choice; read only to migrate to <see cref="TimeZone"/>.</summary>
        public bool? UseLocalTime { get; init; }
    }
}
