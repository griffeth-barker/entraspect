using Entraspect.Core.Checks;
using Microsoft.Extensions.Time.Testing;

namespace Entraspect.Core.Tests.Checks;

public sealed class AppCredentialExpiryCheckTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);

    // Midday, so elapsed days and UTC calendar days differ for end dates with a time of day.
    private static readonly DateTimeOffset Midday = Now.AddHours(12);

    private readonly FakeTimeProvider _time = new(Now);

    private static AppCredential Cred(
        string displayName,
        AppCredentialType type,
        DateTimeOffset? end,
        DateTimeOffset start = default) =>
        new()
        {
            KeyId = Guid.NewGuid(),
            Type = type,
            DisplayName = displayName,
            StartDateTime = start == default ? Now.AddYears(-1) : start,
            EndDateTime = end,
        };

    private static AppRegistration App(string id, params AppCredential[] credentials) =>
        new()
        {
            Id = id,
            AppId = Guid.NewGuid(),
            DisplayName = $"App {id}",
            Credentials = credentials,
        };

    private Task<IReadOnlyList<Finding>> RunAsync(params AppRegistration[] apps) =>
        RunAsync(new CheckOptions(), apps);

    private async Task<IReadOnlyList<Finding>> RunAsync(CheckOptions options, params AppRegistration[] apps)
    {
        var reader = new FakeDirectoryReader(apps);
        var check = new AppCredentialExpiryCheck(_time);
        return await check.RunAsync(reader, options);
    }

    [Fact]
    public async Task Expired_with_no_replacement_is_critical()
    {
        var app = App("1", Cred("secret", AppCredentialType.Secret, Now.AddDays(-5)));

        var findings = await RunAsync(app);

        var f = Assert.Single(findings);
        Assert.Equal("app-credential-expiry", f.CheckId);
        Assert.Equal(Severity.Critical, f.Severity);
        Assert.Equal("Application", f.ObjectType);
        Assert.Equal("1", f.ObjectId);
        Assert.Equal("App 1", f.ObjectDisplayName);
        Assert.Contains("secret", f.Message);
        Assert.Contains("Secret", f.Message);
        Assert.Contains("expired 5 days ago", f.Message);
        Assert.Equal(Now.AddDays(-5), f.DueDate);
    }

    [Fact]
    public async Task Expired_with_valid_same_type_replacement_is_info()
    {
        var app = App(
            "1",
            Cred("old", AppCredentialType.Secret, Now.AddDays(-5)),
            Cred("new", AppCredentialType.Secret, Now.AddDays(60)));

        var findings = await RunAsync(app);

        var f = Assert.Single(findings);
        Assert.Equal(Severity.Info, f.Severity);
        Assert.Contains("newer credential exists", f.Message);
    }

    [Fact]
    public async Task Expired_certificate_with_valid_certificate_replacement_is_info()
    {
        var app = App(
            "1",
            Cred("old", AppCredentialType.Certificate, Now.AddDays(-5)),
            Cred("new", AppCredentialType.Certificate, Now.AddDays(60)));

        var findings = await RunAsync(app);

        var f = Assert.Single(findings);
        Assert.Equal(Severity.Info, f.Severity);
        Assert.Contains("Certificate", f.Message);
    }

    [Fact]
    public async Task Expired_secret_with_only_valid_certificate_is_critical()
    {
        var app = App(
            "1",
            Cred("old", AppCredentialType.Secret, Now.AddDays(-5)),
            Cred("cert", AppCredentialType.Certificate, Now.AddDays(60)));

        var findings = await RunAsync(app);

        var f = Assert.Single(findings);
        Assert.Equal(Severity.Critical, f.Severity);
        Assert.Contains("Secret", f.Message);
        Assert.Contains("expired 5 days ago", f.Message);
    }

    [Fact]
    public async Task Replacement_ending_exactly_now_is_not_valid()
    {
        _time.SetUtcNow(Midday);
        var app = App(
            "1",
            Cred("old", AppCredentialType.Secret, Midday.AddDays(-5)),
            Cred("new", AppCredentialType.Secret, Midday));

        var findings = await RunAsync(app);

        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, f => f.Message.Contains("'old'") && f.Severity == Severity.Critical);
        Assert.Contains(findings, f => f.Message.Contains("'new'") && f.Severity == Severity.Critical);
    }

    [Fact]
    public async Task Expired_with_replacement_in_warning_window_reports_both()
    {
        var app = App(
            "1",
            Cred("old", AppCredentialType.Secret, Now.AddDays(-5)),
            Cred("new", AppCredentialType.Secret, Now.AddDays(20)));

        var findings = await RunAsync(app);

        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, f => f.Message.Contains("'old'") && f.Severity == Severity.Info);
        Assert.Contains(findings, f => f.Message.Contains("'new'") && f.Severity == Severity.Warning);
    }

    [Fact]
    public async Task Expires_within_critical_window_is_critical()
    {
        var app = App("1", Cred("secret", AppCredentialType.Secret, Now.AddDays(3)));

        var findings = await RunAsync(app);

        var f = Assert.Single(findings);
        Assert.Equal(Severity.Critical, f.Severity);
        Assert.Contains("expires in 3 days", f.Message);
    }

    [Fact]
    public async Task Expires_within_warning_window_is_warning()
    {
        var app = App("1", Cred("secret", AppCredentialType.Secret, Now.AddDays(15)));

        var findings = await RunAsync(app);

        var f = Assert.Single(findings);
        Assert.Equal(Severity.Warning, f.Severity);
        Assert.Contains("expires in 15 days", f.Message);
    }

    [Fact]
    public async Task Exactly_on_critical_boundary_is_critical()
    {
        var app = App("1", Cred("secret", AppCredentialType.Secret, Now.AddDays(7)));

        var findings = await RunAsync(app);

        var f = Assert.Single(findings);
        Assert.Equal(Severity.Critical, f.Severity);
        Assert.Contains("expires in 7 days", f.Message);
    }

    [Fact]
    public async Task Just_above_critical_boundary_is_warning()
    {
        var app = App("1", Cred("secret", AppCredentialType.Secret, Now.AddDays(8)));

        var findings = await RunAsync(app);

        var f = Assert.Single(findings);
        Assert.Equal(Severity.Warning, f.Severity);
        Assert.Contains("expires in 8 days", f.Message);
    }

    [Fact]
    public async Task Exactly_on_warning_boundary_is_warning()
    {
        var app = App("1", Cred("secret", AppCredentialType.Secret, Now.AddDays(30)));

        var findings = await RunAsync(app);

        var f = Assert.Single(findings);
        Assert.Equal(Severity.Warning, f.Severity);
        Assert.Contains("expires in 30 days", f.Message);
    }

    [Fact]
    public async Task Beyond_warning_window_has_no_finding()
    {
        var app = App("1", Cred("secret", AppCredentialType.Secret, Now.AddDays(31)));

        var findings = await RunAsync(app);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task No_end_date_has_no_finding()
    {
        var app = App("1", Cred("secret", AppCredentialType.Secret, null));

        var findings = await RunAsync(app);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task Multiple_apps_with_mixed_results()
    {
        var app1 = App("1", Cred("expired", AppCredentialType.Secret, Now.AddDays(-1)));
        var app2 = App("2", Cred("warn", AppCredentialType.Certificate, Now.AddDays(20)));
        var app3 = App("3", Cred("ok", AppCredentialType.Secret, Now.AddDays(120)));

        var findings = await RunAsync(app1, app2, app3);

        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, f => f.ObjectId == "1" && f.Severity == Severity.Critical);
        Assert.Contains(findings, f => f.ObjectId == "2" && f.Severity == Severity.Warning);
        Assert.DoesNotContain(findings, f => f.ObjectId == "3");
    }

    [Fact]
    public async Task Custom_warning_and_critical_days_are_honored()
    {
        var options = new CheckOptions { ExpiryWarningDays = 10, ExpiryCriticalDays = 3 };
        var app = App("1", Cred("secret", AppCredentialType.Secret, Now.AddDays(5)));

        var findings = await RunAsync(options, app);

        var f = Assert.Single(findings);
        Assert.Equal(Severity.Warning, f.Severity);
    }

    [Fact]
    public async Task Later_time_on_critical_boundary_day_is_critical()
    {
        _time.SetUtcNow(Midday);
        var app = App("1", Cred("secret", AppCredentialType.Secret, Midday.AddDays(7).AddHours(6)));

        var findings = await RunAsync(app);

        var f = Assert.Single(findings);
        Assert.Equal(Severity.Critical, f.Severity);
        Assert.Contains("expires in 7 days", f.Message);
    }

    [Fact]
    public async Task Earlier_time_on_critical_boundary_day_counts_as_full_day()
    {
        _time.SetUtcNow(Midday);
        var app = App("1", Cred("secret", AppCredentialType.Secret, Midday.AddDays(7).AddHours(-3)));

        var findings = await RunAsync(app);

        var f = Assert.Single(findings);
        Assert.Equal(Severity.Critical, f.Severity);
        Assert.Contains("expires in 7 days", f.Message);
    }

    [Fact]
    public async Task Later_time_on_warning_boundary_day_is_warning()
    {
        _time.SetUtcNow(Midday);
        var app = App("1", Cred("secret", AppCredentialType.Secret, Midday.AddDays(30).AddHours(6)));

        var findings = await RunAsync(app);

        var f = Assert.Single(findings);
        Assert.Equal(Severity.Warning, f.Severity);
        Assert.Contains("expires in 30 days", f.Message);
    }

    [Fact]
    public async Task Expires_later_today_is_critical_today()
    {
        _time.SetUtcNow(Midday);
        var app = App("1", Cred("secret", AppCredentialType.Secret, Midday.AddHours(6)));

        var findings = await RunAsync(app);

        var f = Assert.Single(findings);
        Assert.Equal(Severity.Critical, f.Severity);
        Assert.Contains("expires today", f.Message);
    }

    [Fact]
    public async Task Ends_exactly_now_is_not_expired()
    {
        _time.SetUtcNow(Midday);
        var app = App("1", Cred("secret", AppCredentialType.Secret, Midday));

        var findings = await RunAsync(app);

        var f = Assert.Single(findings);
        Assert.Equal(Severity.Critical, f.Severity);
        Assert.Contains("expires today", f.Message);
    }

    [Fact]
    public async Task Expired_earlier_today_says_today()
    {
        _time.SetUtcNow(Midday);
        var app = App("1", Cred("secret", AppCredentialType.Secret, Midday.AddHours(-6)));

        var findings = await RunAsync(app);

        var f = Assert.Single(findings);
        Assert.Equal(Severity.Critical, f.Severity);
        Assert.Contains("expired today", f.Message);
    }

    [Fact]
    public async Task One_day_uses_singular()
    {
        var app = App(
            "1",
            Cred("soon", AppCredentialType.Secret, Now.AddDays(1)),
            Cred("gone", AppCredentialType.Certificate, Now.AddDays(-1)));

        var findings = await RunAsync(app);

        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, f => f.Message.Contains("expires in 1 day ("));
        Assert.Contains(findings, f => f.Message.Contains("expired 1 day ago."));
    }

    [Fact]
    public async Task Days_are_counted_in_utc_not_the_end_date_offset()
    {
        _time.SetUtcNow(Midday);

        // 2026-01-23 01:00 at +03:00 is 2026-01-22 22:00 UTC: 7 UTC calendar days away, not 8.
        var end = new DateTimeOffset(2026, 1, 23, 1, 0, 0, TimeSpan.FromHours(3));
        var app = App("1", Cred("secret", AppCredentialType.Secret, end));

        var findings = await RunAsync(app);

        var f = Assert.Single(findings);
        Assert.Equal(Severity.Critical, f.Severity);
        Assert.Contains("expires in 7 days", f.Message);
    }
}
