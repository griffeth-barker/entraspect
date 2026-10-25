using System.IO;
using System.Threading.Tasks;

namespace Entraspect.App.Services;

/// <summary>Opens profiles.json or settings.json for editing in the computer's default app.</summary>
public static class AppFile
{
    /// <summary>
    /// Opens <paramref name="path"/>, first saving it with <paramref name="saveIfMissing"/> if it doesn't exist yet,
    /// so there is something to edit. Returns null on success, otherwise the error to show.
    /// </summary>
    /// <param name="saveIfMissing">Saves the file; returns null on success, otherwise why it couldn't be saved.</param>
    public static async Task<string?> OpenAsync(IUserInteraction ui, string path, Func<string?> saveIfMissing)
    {
        if (!File.Exists(path) && saveIfMissing() is { } saveError)
        {
            return saveError;
        }

        try
        {
            return await ui.OpenFileAsync(path) ? null : $"No app could open '{path}'.";
        }
        catch (Exception ex)
        {
            return $"Couldn't open '{path}': {ex.Message}";
        }
    }
}
