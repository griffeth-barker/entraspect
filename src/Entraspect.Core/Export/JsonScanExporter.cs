using System.Globalization;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using System.Threading;
using System.Threading.Tasks;

namespace Entraspect.Core;

/// <summary>
/// Writes a <see cref="ScanReport"/> as indented JSON: the report details, each check's status and error, and all
/// findings. Property names are camelCase, enums are strings and dates are ISO 8601 UTC, ending in "Z".
/// </summary>
public static class JsonScanExporter
{
    /// <summary>The options the export uses; reading a file back with them gives the same <see cref="ScanReport"/>.</summary>
    public static JsonSerializerOptions SerializerOptions { get; } = CreateOptions();

    public static Task WriteAsync(Stream stream, ScanReport report, CancellationToken cancellationToken = default) =>
        JsonSerializer.SerializeAsync(stream, report, SerializerOptions, cancellationToken);

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,

            // Keep non-ASCII names readable in the file instead of \u escapes.
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new UtcDateTimeOffsetConverter());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    private sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            DateTimeOffset.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(ExportTime.Format(value));
    }
}
