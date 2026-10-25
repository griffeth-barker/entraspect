using System.IO;
using System.Text;
using System.Threading.Tasks;
using Entraspect.Core;

namespace Entraspect.App.Services;

/// <summary>The file the user picked in the save dialog: its name, the format to write and how to open it.</summary>
public sealed record ExportFile(string Name, ExportFormat Format, Func<Task<Stream>> OpenWriteAsync);

public static class ExportFileName
{
    // Not valid in Windows file names, so a file exported on one platform can be copied to another.
    private static readonly char[] InvalidChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    /// <summary>
    /// The suggested name, for example "entraspect-Contoso-20260115-1200.csv". Characters that aren't valid in file
    /// names are removed from the profile name.
    /// </summary>
    /// <param name="stamp">The scan time as "yyyyMMdd-HHmm" (<see cref="TimeDisplay.FileStamp"/>).</param>
    public static string For(string profileName, string stamp, ExportFormat format)
    {
        var profile = new StringBuilder();
        foreach (var c in profileName)
        {
            if (!char.IsControl(c) && !InvalidChars.Contains(c) && !Path.GetInvalidFileNameChars().Contains(c))
            {
                profile.Append(c);
            }
        }

        // Trailing dots and spaces aren't allowed in Windows file names either.
        var name = profile.ToString().Trim().TrimEnd('.');
        return name.Length == 0
            ? $"entraspect-{stamp}.{ScanExport.Extension(format)}"
            : $"entraspect-{name}-{stamp}.{ScanExport.Extension(format)}";
    }

    /// <summary>The format the picked file's extension names, or <paramref name="fallback"/> (the chosen file type).</summary>
    public static ExportFormat FormatFor(string fileName, ExportFormat fallback) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".json" => ExportFormat.Json,
            ".csv" => ExportFormat.Csv,
            _ => fallback,
        };
}
