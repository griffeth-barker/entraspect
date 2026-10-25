using System.IO;
using System.Threading.Tasks;

namespace Entraspect.Core.Tests.Export;

public sealed class ScanReportTests
{
    [Fact]
    public void Follows_the_check_order_and_leaves_out_checks_without_a_result()
    {
        var report = ScanReport.Create(
            new DateTimeOffset(2026, 1, 15, 14, 0, 0, TimeSpan.FromHours(2)),
            "p1",
            ExportTestData.TenantId,
            "1.0.0",
            [ExportTestData.UserCheck, ExportTestData.AppCheck, ExportTestData.CaCheck],
            [
                new CheckResult { CheckId = ExportTestData.CaCheck.Id, Status = CheckResultStatus.Cancelled },
                new CheckResult { CheckId = ExportTestData.UserCheck.Id, Status = CheckResultStatus.Succeeded },
            ]);

        Assert.Equal(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero), report.ScannedAt);
        Assert.Equal(TimeSpan.Zero, report.ScannedAt.Offset);
        Assert.Equal(["user-check", "ca-check"], report.Checks.Select(c => c.CheckId));
    }

    [Fact]
    public void Failed_check_with_a_licence_error_gets_the_plain_message()
    {
        var check = ScanReportCheck.From(ExportTestData.CaCheck, new CheckResult
        {
            CheckId = ExportTestData.CaCheck.Id,
            Status = CheckResultStatus.Failed,
            ErrorMessage = "Tenant does not have a SKU required.",
            StatusCode = 403,
            ErrorCode = "Authentication_RequestFromNonPremiumTenantOrB2CTenant",
        });

        Assert.Equal(CheckFailureMessage.LicenceRequired, check.Error);
        Assert.Equal(403, check.StatusCode);
    }

    [Fact]
    public void Missing_permissions_are_kept_and_there_is_no_error()
    {
        var check = ScanReportCheck.From(ExportTestData.CaCheck, new CheckResult
        {
            CheckId = ExportTestData.CaCheck.Id,
            Status = CheckResultStatus.MissingPermissions,
            MissingPermissions = ["Policy.Read.All"],
        });

        Assert.Null(check.Error);
        Assert.Equal(["Policy.Read.All"], check.MissingPermissions);
    }

    [Fact]
    public void Given_findings_replace_the_results_findings()
    {
        var all = new[] { ExportTestData.ExpiringSecret(), ExportTestData.ExpiringSecret() };
        var result = new CheckResult { CheckId = ExportTestData.AppCheck.Id, Status = CheckResultStatus.Succeeded, Findings = all };

        Assert.Equal(2, ScanReportCheck.From(ExportTestData.AppCheck, result).Findings.Count);
        Assert.Single(ScanReportCheck.From(ExportTestData.AppCheck, result, [all[0]]).Findings);
    }

    [Theory]
    [InlineData(ExportFormat.Csv, "csv")]
    [InlineData(ExportFormat.Json, "json")]
    public async Task Scan_export_writes_the_chosen_format(ExportFormat format, string extension)
    {
        using var stream = new MemoryStream();

        await ScanExport.WriteAsync(stream, ExportTestData.Report(), format);

        Assert.Equal(extension, ScanExport.Extension(format));
        var text = System.Text.Encoding.UTF8.GetString(stream.ToArray()).TrimStart('﻿');
        Assert.Equal(format == ExportFormat.Json ? '{' : 'C', text[0]);
    }
}
