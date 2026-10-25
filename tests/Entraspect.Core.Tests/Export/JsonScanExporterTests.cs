using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Entraspect.Core.Tests.Export;

public sealed class JsonScanExporterTests
{
    [Fact]
    public async Task Round_trips_the_report()
    {
        var report = ExportTestData.Report();

        var json = await WriteAsync(report);
        var read = JsonSerializer.Deserialize<ScanReport>(json, JsonScanExporter.SerializerOptions)!;

        Assert.Equal(report.ScannedAt, read.ScannedAt);
        Assert.Equal("Contoso test", read.ProfileName);
        Assert.Equal(ExportTestData.TenantId, read.TenantId);
        Assert.Equal("1.2.3", read.AppVersion);
        Assert.Equal(["user-check", "app-check", "ca-check"], read.Checks.Select(c => c.CheckId));

        var app = read.Checks[1];
        Assert.Equal("Expiring credentials", app.Title);
        Assert.Equal(CheckCategory.AppRegistrations, app.Category);
        Assert.Equal(CheckResultStatus.Succeeded, app.Status);
        Assert.Null(app.Error);
        var finding = Assert.Single(app.Findings);
        var expected = ExportTestData.ExpiringSecret();
        Assert.Equal(expected.Severity, finding.Severity);
        Assert.Equal(expected.ObjectDisplayName, finding.ObjectDisplayName);
        Assert.Equal(expected.DueDate, finding.DueDate);
        Assert.Equal(expected.SubObjectType, finding.SubObjectType);
        Assert.Equal(expected.SubObjectDisplayName, finding.SubObjectDisplayName);
        Assert.Null(finding.UserPrincipalName);
        Assert.Equal("user1@example.invalid", Assert.Single(read.Checks[0].Findings).UserPrincipalName);

        // Writing what was read gives the same file.
        Assert.Equal(json, await WriteAsync(read));
    }

    [Fact]
    public async Task Includes_the_current_value_of_a_setting()
    {
        var json = await WriteAsync(ExportTestData.SettingsReport());
        using var doc = JsonDocument.Parse(json);

        var finding = doc.RootElement.GetProperty("checks")[0].GetProperty("findings")[0];
        Assert.Equal("Everyone", finding.GetProperty("value").GetString());
        Assert.Equal("ExternalIdentities", doc.RootElement.GetProperty("checks")[0].GetProperty("category").GetString());

        var read = JsonSerializer.Deserialize<ScanReport>(json, JsonScanExporter.SerializerOptions)!;
        Assert.Equal("Everyone", Assert.Single(read.Checks[0].Findings).Value);
    }

    [Fact]
    public async Task Includes_a_failed_check_with_its_error()
    {
        using var doc = JsonDocument.Parse(await WriteAsync(ExportTestData.Report()));

        var failed = doc.RootElement.GetProperty("checks")[2];
        Assert.Equal("ca-check", failed.GetProperty("checkId").GetString());
        Assert.Equal("Failed", failed.GetProperty("status").GetString());
        Assert.Equal(
            "Microsoft Graph returned HTTP 403: Insufficient privileges.",
            failed.GetProperty("error").GetString());
        Assert.Equal(403, failed.GetProperty("statusCode").GetInt32());
        Assert.Equal("Authorization_RequestDenied", failed.GetProperty("errorCode").GetString());
        Assert.Equal(0, failed.GetProperty("findings").GetArrayLength());
    }

    [Fact]
    public async Task Writes_enums_as_strings_and_dates_as_iso_8601_utc()
    {
        using var doc = JsonDocument.Parse(await WriteAsync(ExportTestData.Report()));

        var root = doc.RootElement;
        Assert.Equal("2026-01-15T12:30:00Z", root.GetProperty("scannedAt").GetString());
        var app = root.GetProperty("checks")[1];
        Assert.Equal("AppRegistrations", app.GetProperty("category").GetString());
        var finding = app.GetProperty("findings")[0];
        Assert.Equal("Critical", finding.GetProperty("severity").GetString());

        // 09:00 at UTC+2 is written as 07:00 UTC.
        Assert.Equal("2026-01-18T07:00:00Z", finding.GetProperty("dueDate").GetString());
    }

    [Fact]
    public async Task Keeps_values_as_they_are_and_non_ascii_readable()
    {
        var json = await WriteAsync(ExportTestData.Report());

        Assert.Contains("Zoë", json, StringComparison.Ordinal);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(
            "=HYPERLINK(\"http://example.invalid\")",
            doc.RootElement.GetProperty("checks")[0].GetProperty("findings")[0].GetProperty("objectDisplayName").GetString());
    }

    [Fact]
    public async Task Report_holds_only_scan_details_and_checks()
    {
        using var doc = JsonDocument.Parse(await WriteAsync(ExportTestData.Report()));

        Assert.Equal(
            ["scannedAt", "profileName", "tenantId", "appVersion", "checks"],
            doc.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal(
            ["checkId", "title", "category", "status", "error", "statusCode", "errorCode", "missingPermissions",
                "completedAt", "findings"],
            doc.RootElement.GetProperty("checks")[0].EnumerateObject().Select(p => p.Name));
    }

    private static async Task<string> WriteAsync(ScanReport report)
    {
        using var stream = new MemoryStream();
        await JsonScanExporter.WriteAsync(stream, report);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
