using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using Entraspect.App.Services;
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
    public void Add_filter_on_severity_keeps_only_the_chosen_values()
    {
        var page = CreatePageWithThreeFindings();

        page.AddFilterCommand.Execute(null);
        Assert.True(page.IsFilterEditorOpen);
        Assert.True(page.IsChoosingField);
        page.ChooseFieldCommand.Execute(Field(page, "Severity"));
        Assert.True(page.IsEditingChoice);
        Assert.Equal(new[] { "Critical", "Warning", "Info" }, page.EditorChoices.Select(c => c.Value));
        Assert.False(page.ApplyFilterCommand.CanExecute(null));

        page.EditorChoices.Single(c => c.Value == "Critical").IsSelected = true;
        page.EditorChoices.Single(c => c.Value == "Info").IsSelected = true;
        page.ApplyFilterCommand.Execute(null);

        Assert.False(page.IsFilterEditorOpen);
        Assert.Equal("Severity == Critical, Info", Assert.Single(page.ActiveFilters).DisplayText);
        Assert.Equal(new[] { Severity.Critical, Severity.Info }, page.Findings.Select(f => f.Severity));
        Assert.Equal("Showing 2 of 3 finding(s)", page.FilterSummary);
    }

    [Fact]
    public void Text_filters_match_part_of_the_value_and_combine_with_other_filters()
    {
        var page = CreatePageWithThreeFindings();

        page.AddFilterCommand.Execute(null);
        page.ChooseFieldCommand.Execute(Field(page, "Display name"));
        Assert.True(page.IsEditingText);
        page.EditorText = "payroll";
        page.ApplyFilterCommand.Execute(null);
        Assert.Equal(2, page.Findings.Count);
        Assert.Equal("Display name contains \"payroll\"", page.ActiveFilters[0].DisplayText);

        page.AddFilterCommand.Execute(null);
        page.ChooseFieldCommand.Execute(Field(page, "Credential"));
        page.EditorText = "cert";
        page.ApplyFilterCommand.Execute(null);

        Assert.Equal("cert", Assert.Single(page.Findings).SubObjectDisplayName);
    }

    [Fact]
    public void Choosing_a_field_that_already_has_a_filter_edits_it()
    {
        var page = CreatePageWithThreeFindings();
        page.AddFilterCommand.Execute(null);
        page.ChooseFieldCommand.Execute(Field(page, "Severity"));
        page.EditorChoices.Single(c => c.Value == "Critical").IsSelected = true;
        page.ApplyFilterCommand.Execute(null);

        page.AddFilterCommand.Execute(null);
        page.ChooseFieldCommand.Execute(Field(page, "Severity"));
        Assert.True(page.EditorChoices.Single(c => c.Value == "Critical").IsSelected);
        page.EditorChoices.Single(c => c.Value == "Warning").IsSelected = true;
        page.ApplyFilterCommand.Execute(null);

        Assert.Equal("Severity == Critical, Warning", Assert.Single(page.ActiveFilters).DisplayText);
        Assert.Equal(2, page.Findings.Count);
    }

    [Fact]
    public void Type_choices_come_from_the_findings()
    {
        var page = CreatePageWithThreeFindings();

        page.AddFilterCommand.Execute(null);
        page.ChooseFieldCommand.Execute(Field(page, "Type"));

        Assert.Equal(new[] { "Certificate", "Secret" }, page.EditorChoices.Select(c => c.Value));
    }

    [Fact]
    public void Remove_reset_and_search()
    {
        var page = CreatePageWithThreeFindings();
        page.AddFilterCommand.Execute(null);
        page.ChooseFieldCommand.Execute(Field(page, "Message"));
        page.EditorText = "newer";
        page.ApplyFilterCommand.Execute(null);
        Assert.Single(page.Findings);

        page.RemoveFilterCommand.Execute(page.ActiveFilters[0]);
        Assert.Equal(3, page.Findings.Count);

        page.SearchText = "SECRET";
        Assert.Equal(2, page.Findings.Count);
        page.SearchText = "obj-hr";
        Assert.Equal("HR portal", Assert.Single(page.Findings).ObjectDisplayName);

        page.ResetFiltersCommand.Execute(null);
        Assert.Equal(3, page.Findings.Count);
        Assert.Equal(string.Empty, page.SearchText);
    }

    [Fact]
    public void User_principal_name_is_its_own_column_filter_and_search_field()
    {
        var page = new CheckPageViewModel(
            new FakeCheck(
                ["AuditLog.Read.All"],
                new FindingLabels
                {
                    UserPrincipalName = "User principal name",
                    SubObjectDisplayName = null,
                    SubObjectType = null,
                    DueDate = null,
                }),
            new PermissionBannerViewModel(() => Task.CompletedTask, _ => Task.CompletedTask, new RelayCommand(() => { })),
            new TimeDisplay(new SettingsState(new SettingsStore(TempDir()))));
        page.SetResult(Succeeded(
            UserFinding("1", "Alex Wilber", "alexw@contoso.example"),
            UserFinding("2", "Alex Wilber", "alex.wilber@contoso.example")));

        Assert.Equal(
            new[] { "Severity", "Display name", "User principal name", "Object ID", "Message" },
            page.FilterFields.Select(f => f.Name));
        Assert.Equal("alexw@contoso.example", page.Findings[0].UserPrincipalName);

        page.SearchText = "alex.wilber@";
        Assert.Equal("2", Assert.Single(page.Findings).ObjectId);

        page.SearchText = string.Empty;
        page.AddFilterCommand.Execute(null);
        page.ChooseFieldCommand.Execute(Field(page, "User principal name"));
        page.EditorText = "alexw@";
        page.ApplyFilterCommand.Execute(null);
        Assert.Equal("1", Assert.Single(page.Findings).ObjectId);
    }

    [Fact]
    public void Columns_and_filters_are_named_by_the_checks_labels()
    {
        var page = CreatePage();

        Assert.Equal(
            new[] { "Severity", "Display name", "Object ID", "Credential", "Type", "Message" },
            page.FilterFields.Select(f => f.Name));
        Assert.Equal("Expires (UTC)", page.DueHeader);
    }

    [Fact]
    public void Dates_follow_the_local_time_preference()
    {
        var settings = new SettingsState(new SettingsStore(TempDir()));
        var page = CreatePage(new TimeDisplay(settings, new TimeZones(PlusTwoHours, [])));
        settings.SetTimeZone(AppSettings.LocalTimeZone);

        page.SetResult(Succeeded(Finding(Severity.Critical, "a", 1)));

        Assert.Equal("Expires (local)", page.DueHeader);
        Assert.Equal("Last run 2026-01-15 02:00", page.StatusText);
        Assert.Equal("2026-01-16", Assert.Single(page.Findings).DueText);
    }

    [Fact]
    public void Status_shows_when_the_report_was_updated()
    {
        var page = CreatePage();

        page.SetResult(new CheckResult
        {
            CheckId = "fake",
            Status = CheckResultStatus.Succeeded,
            CompletedAt = Now,
            DataUpdatedAt = Now.AddHours(-6),
        });

        Assert.Equal("Last run 2026-01-15 00:00 UTC · Report updated 2026-01-14 18:00 UTC", page.StatusText);
    }

    [Fact]
    public void Status_shows_the_period_of_logs_read()
    {
        var page = CreatePage();

        page.SetResult(new CheckResult
        {
            CheckId = "fake",
            Status = CheckResultStatus.Succeeded,
            CompletedAt = Now,
            DataPeriod = new DataPeriod(Now.AddDays(-7), Now.AddMinutes(-5), 12345, "sign-in"),
        });

        Assert.Equal(
            "Last run 2026-01-15 00:00 UTC · 12,345 sign-in(s) from 2026-01-08 00:00 UTC to 2026-01-14 23:55 UTC",
            page.StatusText);
    }

    [Fact]
    public void Banner_shows_only_the_missing_permissions_while_connected()
    {
        var page = CreatePage("A.Read.All", "B.Read.All");
        page.SetConnection(null, TenantLicence.Unknown);
        Assert.False(page.Banner.IsOpen);

        page.SetConnection(new[] { "A.Read.All" }, TenantLicence.Unknown);

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

    [Fact]
    public void A_licence_failure_says_the_licence_is_needed_not_the_permission()
    {
        var page = CreatePage();

        page.SetResult(new CheckResult
        {
            CheckId = "fake",
            Status = CheckResultStatus.Failed,
            ErrorMessage = "Microsoft Graph returned HTTP 403: Tenant does not have a SKU required.",
            StatusCode = 403,
            ErrorCode = "Authentication_RequestFromNonPremiumTenantOrB2CTenant",
        });

        Assert.Equal("This check needs an Entra ID P1 or P2 licence.", page.ErrorText);
    }

    private static readonly TimeZoneInfo PlusTwoHours =
        TimeZoneInfo.CreateCustomTimeZone("Test+2", TimeSpan.FromHours(2), "Test+2", "Test+2");

    private static string TempDir() => Path.Combine(Path.GetTempPath(), $"entraspect-page-{Guid.NewGuid()}");

    private static CheckPageViewModel CreatePage(params string[] permissions) =>
        CreatePage(new TimeDisplay(new SettingsState(new SettingsStore(TempDir()))), permissions);

    private static CheckPageViewModel CreatePage(TimeDisplay time, params string[] permissions) => new(
        new FakeCheck(permissions.Length == 0 ? ["Application.Read.All"] : permissions),
        new PermissionBannerViewModel(() => Task.CompletedTask, _ => Task.CompletedTask, new RelayCommand(() => { })),
        time);

    private static CheckPageViewModel CreatePageWithThreeFindings()
    {
        var page = CreatePage();
        page.SetResult(Succeeded(
            Finding(Severity.Critical, "Payroll", 1, item: "prod secret", type: "Secret"),
            Finding(Severity.Warning, "Payroll", 10, item: "cert", type: "Certificate"),
            Finding(Severity.Info, "HR portal", 30, item: "old", type: "Secret", message: "old secret, newer exists")));
        return page;
    }

    private static FilterField Field(CheckPageViewModel page, string name) => page.FilterFields.Single(f => f.Name == name);

    private static CheckResult Succeeded(params Finding[] findings) => new()
    {
        CheckId = "fake", Status = CheckResultStatus.Succeeded, Findings = findings, CompletedAt = Now,
    };

    private static Finding Finding(
        Severity severity, string obj, int? dueInDays, string? item = null, string message = "fake message",
        string? type = null) => new()
    {
        CheckId = "fake",
        Severity = severity,
        ObjectType = "Application",
        ObjectId = $"obj-{obj.ToLowerInvariant().Replace(' ', '-')}",
        SubObjectType = type,
        ObjectDisplayName = obj,
        SubObjectDisplayName = item,
        Message = message,
        DueDate = dueInDays is { } days ? Now.AddDays(days) : null,
    };

    private static Finding UserFinding(string id, string displayName, string userPrincipalName) => new()
    {
        CheckId = "fake",
        Severity = Severity.Warning,
        ObjectType = "User",
        ObjectId = id,
        ObjectDisplayName = displayName,
        UserPrincipalName = userPrincipalName,
        Message = "Not registered for MFA.",
    };

    private sealed class FakeCheck(IReadOnlyList<string> permissions, FindingLabels? labels = null) : ICheck
    {
        public string Id => "fake";

        public string Title => "Fake check";

        public string Description => "A check for tests.";

        public CheckCategory Category => CheckCategory.AppRegistrations;

        public IReadOnlyList<string> RequiredPermissions => permissions;

        public FindingLabels Labels { get; } =
            labels ?? new() { SubObjectDisplayName = "Credential", DueDate = "Expires" };

        public Task<IReadOnlyList<Finding>> RunAsync(
            IDirectoryReader reader, CheckOptions options, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Finding>>([]);
    }
}
