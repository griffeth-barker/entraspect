using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Entraspect.App.ViewModels;
using Entraspect.Core;

namespace Entraspect.App.Tests;

public sealed class CheckPageViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Findings_are_sorted_by_severity_then_due_date()
    {
        var page = CreatePage();

        page.SetResult(Succeeded(
            Finding(Severity.Warning, "w-late", 20),
            Finding(Severity.Critical, "c-late", 5),
            Finding(Severity.Info, "i", null),
            Finding(Severity.Critical, "c-soon", 1),
            Finding(Severity.Warning, "w-soon", 10)));

        Assert.Equal(
            new[] { "c-soon", "c-late", "w-soon", "w-late", "i" },
            page.Findings.Select(f => f.ObjectDisplayName));
        Assert.Equal("5 finding(s)", page.FilterSummary);
    }

    [Fact]
    public void Severity_toggles_and_text_filter_narrow_the_findings()
    {
        var page = CreatePage();
        page.SetResult(Succeeded(
            Finding(Severity.Critical, "Payroll", 1, item: "prod secret"),
            Finding(Severity.Warning, "Payroll", 10, item: "cert"),
            Finding(Severity.Info, "HR portal", 30, message: "old secret, newer exists")));

        page.ShowWarning = false;
        Assert.Equal(new[] { Severity.Critical, Severity.Info }, page.Findings.Select(f => f.Severity));

        page.FilterText = "SECRET";
        Assert.Equal(2, page.Findings.Count);

        page.ShowInfo = false;
        Assert.Equal("prod secret", Assert.Single(page.Findings).SubObjectDisplayName);
        Assert.Equal("Showing 1 of 3 finding(s)", page.FilterSummary);

        page.FilterText = "hr portal";
        Assert.Empty(page.Findings);
    }

    [Fact]
    public void Required_permissions_are_marked_granted_or_missing()
    {
        var page = CreatePage("A.Read.All", "B.Read.All");

        Assert.All(page.Permissions, p => Assert.Equal(string.Empty, p.StatusText));
        Assert.False(page.Banner.IsOpen);

        page.SetGrantedPermissions(new[] { "A.Read.All" });

        Assert.Equal(new[] { "Granted", "Missing" }, page.Permissions.Select(p => p.StatusText));
        Assert.True(page.Banner.IsOpen);
        Assert.Equal(new[] { "B.Read.All" }, page.Banner.MissingPermissions);
    }

    [Fact]
    public void Banner_text_names_every_missing_permission()
    {
        Assert.Equal(
            "This check needs the Microsoft Graph application permissions A.Read.All, B.Read.All, which aren't " +
            "granted to this app registration. Add them and grant admin consent, then reconnect.",
            PermissionBannerViewModel.CheckMessage(["A.Read.All", "B.Read.All"]));
    }

    [Fact]
    public void A_403_failure_names_the_likely_missing_permission()
    {
        var page = CreatePage();

        page.SetResult(new CheckResult
        {
            CheckId = "fake", Status = CheckResultStatus.Failed, ErrorMessage = "Forbidden", StatusCode = 403,
        });

        Assert.Contains("Application.Read.All", page.ErrorText);
        Assert.Empty(page.Findings);
    }

    private static CheckPageViewModel CreatePage(params string[] permissions) => new(
        new FakeCheck(permissions.Length == 0 ? ["Application.Read.All"] : permissions),
        new PermissionBannerViewModel(() => Task.CompletedTask, _ => Task.CompletedTask, new RelayCommand(() => { })));

    private static CheckResult Succeeded(params Finding[] findings) => new()
    {
        CheckId = "fake", Status = CheckResultStatus.Succeeded, Findings = findings, CompletedAt = Now,
    };

    private static Finding Finding(
        Severity severity, string obj, int? dueInDays, string? item = null, string message = "fake message") => new()
    {
        CheckId = "fake",
        Severity = severity,
        ObjectType = "Application",
        ObjectId = obj,
        ObjectDisplayName = obj,
        SubObjectDisplayName = item,
        Message = message,
        DueDate = dueInDays is { } days ? Now.AddDays(days) : null,
    };

    private sealed class FakeCheck(IReadOnlyList<string> permissions) : ICheck
    {
        public string Id => "fake";

        public string Title => "Fake check";

        public string Description => "A check for tests.";

        public CheckCategory Category => CheckCategory.AppRegistrations;

        public IReadOnlyList<string> RequiredPermissions => permissions;

        public Task<IReadOnlyList<Finding>> RunAsync(
            IDirectoryReader reader, CheckOptions options, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Finding>>([]);
    }
}
