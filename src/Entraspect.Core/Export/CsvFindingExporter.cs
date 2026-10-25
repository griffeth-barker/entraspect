using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Entraspect.Core;

/// <summary>
/// Writes a <see cref="ScanReport"/>'s findings as CSV, one row per finding: UTF-8 with a byte order mark so Excel
/// reads non-ASCII names, RFC 4180 quoting and CRLF line ends. Dates are ISO 8601 UTC, ending in "Z".
/// </summary>
public static class CsvFindingExporter
{
    public static readonly IReadOnlyList<string> Columns =
    [
        "CheckId",
        "CheckTitle",
        "Category",
        "Severity",
        "ObjectType",
        "ObjectId",
        "ObjectDisplayName",
        "UserPrincipalName",
        "SubObjectType",
        "SubObjectDisplayName",
        "DueDate",
        "Message",
    ];

    // A spreadsheet app may run a cell starting with one of these as a formula.
    private static readonly char[] FormulaStarts = ['=', '+', '-', '@', '\t', '\r'];

    public static async Task WriteAsync(Stream stream, ScanReport report, CancellationToken cancellationToken = default)
    {
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true)
        {
            NewLine = "\r\n",
        };

        await WriteRowAsync(writer, Columns, cancellationToken).ConfigureAwait(false);
        foreach (var check in report.Checks)
        {
            foreach (var finding in check.Findings)
            {
                await WriteRowAsync(
                    writer,
                    [
                        check.CheckId,
                        check.Title,
                        check.Category.ToString(),
                        finding.Severity.ToString(),
                        finding.ObjectType,
                        finding.ObjectId,
                        finding.ObjectDisplayName,
                        finding.UserPrincipalName,
                        finding.SubObjectType,
                        finding.SubObjectDisplayName,
                        finding.DueDate is { } due ? ExportTime.Format(due) : null,
                        finding.Message,
                    ],
                    cancellationToken).ConfigureAwait(false);
            }
        }

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// One cell: a value that a spreadsheet app could run as a formula gets a leading single quote, then a value with a
    /// comma, quote or line break is quoted with its quotes doubled. Null is an empty cell.
    /// </summary>
    public static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (FormulaStarts.Contains(value[0]))
        {
            value = "'" + value;
        }

        return value.AsSpan().IndexOfAny(",\"\r\n") >= 0
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
    }

    private static async Task WriteRowAsync(StreamWriter writer, IReadOnlyList<string?> values, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await writer.WriteLineAsync(string.Join(",", values.Select(Cell))).ConfigureAwait(false);
    }
}

/// <summary>How exports write dates and times: ISO 8601 in UTC, for example "2026-01-15T12:00:00Z".</summary>
internal static class ExportTime
{
    public static string Format(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture);
}
