using System;
using System.IO;

namespace Entraspect.App.Services;

/// <summary>
/// Copies an unreadable store file aside so a later save does not destroy it.
/// </summary>
internal static class StoreBackup
{
    /// <summary>
    /// Returns the backup path, or null with <paramref name="failure"/> set to the reason if no backup could be made.
    /// </summary>
    public static string? Backup(string filePath, string directory, out string? failure)
    {
        var backupPath = Path.Combine(
            directory,
            $"{Path.GetFileNameWithoutExtension(filePath)}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmssfff}.json");

        try
        {
            File.Copy(filePath, backupPath);
            failure = null;
            return backupPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failure = ex.Message;
            return null;
        }
    }
}
