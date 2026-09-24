using System;
using System.IO;

namespace Entraspect.App.Services;

/// <summary>
/// Copies an unreadable store file aside so a later save does not destroy it. Returns the backup path, or null if one
/// could not be made.
/// </summary>
internal static class StoreBackup
{
    public static string? Backup(string filePath, string directory)
    {
        var backupPath = Path.Combine(
            directory,
            $"{Path.GetFileNameWithoutExtension(filePath)}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}.json");

        try
        {
            File.Copy(filePath, backupPath);
            return backupPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
