using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Entraspect.Core;

public enum ExportFormat
{
    /// <summary>One row per finding, for spreadsheets.</summary>
    Csv,

    /// <summary>The whole report: details, each check's status and error, and findings.</summary>
    Json,
}

public static class ScanExport
{
    /// <summary>The file extension without the dot, for example "csv".</summary>
    public static string Extension(ExportFormat format) => format == ExportFormat.Json ? "json" : "csv";

    public static Task WriteAsync(
        Stream stream,
        ScanReport report,
        ExportFormat format,
        CancellationToken cancellationToken = default) => format == ExportFormat.Json
        ? JsonScanExporter.WriteAsync(stream, report, cancellationToken)
        : CsvFindingExporter.WriteAsync(stream, report, cancellationToken);
}
