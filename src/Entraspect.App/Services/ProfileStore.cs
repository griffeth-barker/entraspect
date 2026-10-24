using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Entraspect.Graph;

namespace Entraspect.App.Services;

/// <summary>
/// Saves connection profiles as JSON in the user's application data folder. Profiles never contain passwords.
/// </summary>
public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _directory;
    private readonly string _filePath;

    public ProfileStore() : this(DefaultDirectory())
    {
    }

    /// <summary>Uses <paramref name="directory"/> for storage instead of the application data folder.</summary>
    public ProfileStore(string directory)
    {
        _directory = directory;
        _filePath = Path.Combine(_directory, "profiles.json");
    }

    /// <summary>
    /// Loads saved profiles. Returns an empty list and sets <paramref name="error"/> if the file cannot be read.
    /// An unreadable file is copied aside first, so a later save does not destroy it. Unknown fields (for example a
    /// legacy <c>authMode</c>) are ignored.
    /// </summary>
    public IReadOnlyList<ConnectionProfile> Load(out string? error)
    {
        error = null;
        if (!File.Exists(_filePath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<List<ConnectionProfile>>(json, Options) ?? [];
        }
        catch (JsonException)
        {
            var backupPath = StoreBackup.Backup(_filePath, _directory, out var failure);
            error = backupPath is not null
                ? $"Saved profiles could not be read. The file was backed up to '{backupPath}'."
                : $"Saved profiles in '{_filePath}' could not be read, and a backup could not be made: {failure}";
            return [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = $"Saved profiles could not be read: {ex.Message}";
            return [];
        }
    }

    public void Save(IEnumerable<ConnectionProfile> profiles)
    {
        Directory.CreateDirectory(_directory);
        var json = JsonSerializer.Serialize(profiles, Options);
        File.WriteAllText(_filePath, json);
    }

    private static string DefaultDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "entraspect");
}
