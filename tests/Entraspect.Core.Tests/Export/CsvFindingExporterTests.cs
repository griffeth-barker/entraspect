using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Entraspect.Core.Tests.Export;

public sealed class CsvFindingExporterTests
{
    private const string Header =
        "CheckId,CheckTitle,Category,Severity,ObjectType,ObjectId,ObjectDisplayName,UserPrincipalName,SubObjectType," +
        "SubObjectDisplayName,Value,DueDate,Message";

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("line1\nline2", "\"line1\nline2\"")]
    [InlineData("line1\r\nline2", "\"line1\r\nline2\"")]
    [InlineData("Zoë", "Zoë")]
    public void Quotes_commas_quotes_and_new_lines(string value, string expected)
    {
        Assert.Equal(expected, CsvFindingExporter.Cell(value));
    }

    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+1", "'+1")]
    [InlineData("-1", "'-1")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("\tcmd", "'\tcmd")]
    [InlineData("\rcmd", "\"'\rcmd\"")]
    [InlineData("=A1,B1", "\"'=A1,B1\"")]
    [InlineData("a=b", "a=b")]
    [InlineData("'quoted", "'quoted")]
    public void Prefixes_values_a_spreadsheet_could_run_as_a_formula(string value, string expected)
    {
        Assert.Equal(expected, CsvFindingExporter.Cell(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Missing_values_are_empty_cells(string? value)
    {
        Assert.Equal(string.Empty, CsvFindingExporter.Cell(value));
    }

    [Fact]
    public async Task Writes_utf8_with_bom_a_header_and_one_row_per_finding_in_check_order()
    {
        var bytes = await WriteAsync(ExportTestData.Report());

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        var lines = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Split("\r\n");
        Assert.Equal(
            [
                Header,
                "user-check,MFA not registered,Users,Warning,User,00000000-0000-0000-0000-0000000000u1," +
                "\"'=HYPERLINK(\"\"http://example.invalid\"\")\",user1@example.invalid,,,,,No MFA method registered.",
                "app-check,Expiring credentials,AppRegistrations,Critical,Application,00000000-0000-0000-0000-0000000000a1," +
                "\"Zoë's app, \"\"prod\"\"\",,Secret,CI key,,2026-01-18T07:00:00Z,Secret expires in 3 days.",
                string.Empty,
            ],
            lines);
    }

    [Fact]
    public async Task Writes_the_category_by_name()
    {
        var bytes = await WriteAsync(ExportTestData.RolesReport());

        var lines = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Split("\r\n");
        Assert.StartsWith("roles-check,Admin role counts,RolesAndAdmins,", lines[1]);
    }

    [Fact]
    public async Task Empty_optional_fields_keep_every_column()
    {
        var report = ExportTestData.Report() with
        {
            Checks =
            [
                ScanReportCheck.From(
                    ExportTestData.AppCheck,
                    new CheckResult { CheckId = ExportTestData.AppCheck.Id, Status = CheckResultStatus.Succeeded },
                    [
                        new Finding
                        {
                            CheckId = ExportTestData.AppCheck.Id,
                            Severity = Severity.Info,
                            ObjectType = "Application",
                            ObjectId = "id1",
                            ObjectDisplayName = "App",
                            Message = "No owners.",
                        },
                    ]),
            ],
        };

        var text = Encoding.UTF8.GetString(await WriteAsync(report)).TrimStart('﻿');

        Assert.Equal(
            $"{Header}\r\napp-check,Expiring credentials,AppRegistrations,Info,Application,id1,App,,,,,,No owners.\r\n",
            text);
    }

    [Fact]
    public async Task Writes_the_current_value_of_a_setting()
    {
        var text = Encoding.UTF8.GetString(await WriteAsync(ExportTestData.SettingsReport())).TrimStart('\uFEFF');

        Assert.Equal(
            $"{Header}\r\nsettings-check,External collaboration,ExternalIdentities,Warning,AuthorizationPolicy," +
            "allowInvitesFrom,Guest invite restrictions,,,,Everyone,," +
            "\"Anyone in the organisation, guests included, can invite guests.\"\r\n",
            text);
    }

    [Fact]
    public async Task A_report_without_findings_has_only_the_header()
    {
        var report = ExportTestData.Report() with { Checks = [] };

        var text = Encoding.UTF8.GetString(await WriteAsync(report)).TrimStart('﻿');

        Assert.Equal(Header + "\r\n", text);
    }

    [Fact]
    public async Task Leaves_the_stream_open()
    {
        using var stream = new MemoryStream();

        await CsvFindingExporter.WriteAsync(stream, ExportTestData.Report());

        Assert.True(stream.CanWrite);
    }

    private static async Task<byte[]> WriteAsync(ScanReport report)
    {
        using var stream = new MemoryStream();
        await CsvFindingExporter.WriteAsync(stream, report);
        return stream.ToArray();
    }
}
