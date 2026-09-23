using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Entraspect.Graph;

namespace Entraspect.App.Services;

public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;

    public ProfileStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "entraspect");
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "profiles.json");
    }

    public IReadOnlyList<ConnectionProfile> Load()
    {
        if (!File.Exists(_filePath))
        {
            return new List<ConnectionProfile>();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<List<ConnectionProfile>>(json, Options)
                   ?? new List<ConnectionProfile>();
        }
        catch (JsonException)
        {
            return new List<ConnectionProfile>();
        }
    }

    public void Save(IEnumerable<ConnectionProfile> profiles)
    {
        var json = JsonSerializer.Serialize(profiles, Options);
        File.WriteAllText(_filePath, json);
    }
}
