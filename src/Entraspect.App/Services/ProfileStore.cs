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
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _directory;
    private readonly string _filePath;

    public ProfileStore()
    {
        _directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "entraspect");
        _filePath = Path.Combine(_directory, "profiles.json");
    }

    /// <summary>
    /// Loads saved profiles. Returns an empty list and sets <paramref name="error"/> if the file cannot be read.
    /// An unreadable file is copied aside first, so a later save does not destroy it.
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
            var backupPath = Path.Combine(_directory, $"profiles.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}.json");
            try
            {
                File.Copy(_filePath, backupPath);
                error = $"Saved profiles could not be read. The file was backed up to '{backupPath}'.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error = $"Saved profiles in '{_filePath}' could not be read, and a backup could not be made: {ex.Message}";
            }

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
}
