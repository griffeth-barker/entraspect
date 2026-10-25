using Entraspect.Core.Checks;
using Microsoft.Extensions.Time.Testing;

namespace Entraspect.Core.Tests.Checks;

public sealed class SamlSigningCertificateExpiryCheckTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);

    // Midday, so elapsed days and UTC calendar days differ for end dates with a time of day.
    private static readonly DateTimeOffset Midday = Now.AddHours(12);

    private readonly FakeTimeProvider _time = new(Now);

    private static SigningCertificate Cert(
        string thumbprint,
        DateTimeOffset? end,
        string? displayName = null) =>
        new()
        {
            KeyId = Guid.NewGuid(),
            Thumbprint = thumbprint,
            DisplayName = displayName ?? $"CN=cert {thumbprint}",
            StartDateTime = Now.AddYears(-3),
            EndDateTime = end,
        };

    private static EnterpriseApp App(
        string id,
        string? activeThumbprint,
        params SigningCertificate[] certificates) =>
        App(id, activeThumbprint, "saml", enabled: true, certificates);

    private static EnterpriseApp App(
        string id,
        string? activeThumbprint,
        string? mode,
        bool enabled,
        params SigningCertificate[] certificates) =>
        new()
        {
            Id = id,
            AppId = Guid.NewGuid(),
            DisplayName = $"App {id}",
            AccountEnabled = enabled,
            PreferredSingleSignOnMode = mode,
            PreferredTokenSigningKeyThumbprint = activeThumbprint,
            SigningCertificates = certificates,
        };

    private Task<IReadOnlyList<Finding>> RunAsync(params EnterpriseApp[] apps) =>
        RunAsync(new CheckOptions(), apps);

    private async Task<IReadOnlyList<Finding>> RunAsync(CheckOptions options, params EnterpriseApp[] apps)
    {
        var reader = new FakeDirectoryReader(apps);
        var check = new SamlSigningCertificateExpiryCheck(_time);
        return await check.RunAsync(reader, options);
    }

    [Fact]
    public async Task Active_expired_is_critical()
    {
        var app = App("1", "AAAA", Cert("AAAA", Now.AddDays(-5), "CN=Contoso SSO"));

        var findings = await RunAsync(app);

        var f = Assert.Single(findings);
        Assert.Equal("saml-signing-certificate-expiry", f.CheckId);
        Assert.Equal(Severity.Critical, f.Severity);
        Assert.Equal("EnterpriseApp", f.ObjectType);
        Assert.Equal("1", f.ObjectId);
        Assert.Equal("App 1", f.ObjectDisplayName);
        Assert.Equal(
            "Active signing certificate expired 5 days ago. SAML sign-in to this app may be failing.",
            f.Message);
        Assert.Equal(Now.AddDays(-5), f.DueDate);
        Assert.Equal("Signing certificate", f.SubObjectType);
        Assert.Equal("CN=Contoso SSO", f.SubObjectDisplayName);
    }

    [Fact]
    public async Task Active_expired_earlier_today_says_today()
    {
        _time.SetUtcNow(Midday);
        var app = App("1", "AAAA", Cert("AAAA", Midday.AddHours(-6)));

        var f = Assert.Single(await RunAsync(app));

        Assert.Equal(Severity.Critical, f.Severity);
        Assert.StartsWith("Active signing certificate expired today.", f.Message);
    }

    [Theory]
    [InlineData(0, Severity.Critical, "expires today")]
    [InlineData(3, Severity.Critical, "expires in 3 days")]
    [InlineData(7, Severity.Critical, "expires in 7 days")]
    [InlineData(8, Severity.Warning, "expires in 8 days")]
    [InlineData(15, Severity.Warning, "expires in 15 days")]
    [InlineData(30, Severity.Warning, "expires in 30 days")]
    public async Task Active_in_a_window_has_its_severity(int days, Severity severity, string phrase)
    {
        var app = App("1", "AAAA", Cert("AAAA", Now.AddDays(days)));

        var f = Assert.Single(await RunAsync(app));

        Assert.Equal(severity, f.Severity);
        Assert.Contains($"Active signing certificate {phrase}", f.Message);
        Assert.Equal(Now.AddDays(days), f.DueDate);
    }

    [Fact]
    public async Task Active_beyond_warning_window_has_no_finding()
    {
        var app = App("1", "AAAA", Cert("AAAA", Now.AddDays(31)));

        Assert.Empty(await RunAsync(app));
    }

    [Fact]
    public async Task Later_time_on_critical_boundary_day_is_critical()
    {
        _time.SetUtcNow(Midday);
        var app = App("1", "AAAA", Cert("AAAA", Midday.AddDays(7).AddHours(6)));

        var f = Assert.Single(await RunAsync(app));

        Assert.Equal(Severity.Critical, f.Severity);
        Assert.Contains("expires in 7 days", f.Message);
    }

    [Fact]
    public async Task Later_time_on_warning_boundary_day_is_warning()
    {
        _time.SetUtcNow(Midday);
        var app = App("1", "AAAA", Cert("AAAA", Midday.AddDays(30).AddHours(6)));

        var f = Assert.Single(await RunAsync(app));

        Assert.Equal(Severity.Warning, f.Severity);
        Assert.Contains("expires in 30 days", f.Message);
    }

    [Fact]
    public async Task Custom_windows_are_honored()
    {
        var options = new CheckOptions { ExpiryWarningDays = 60, ExpiryCriticalDays = 14 };
        var app = App("1", "AAAA", Cert("AAAA", Now.AddDays(45)));

        var f = Assert.Single(await RunAsync(options, app));

        Assert.Equal(Severity.Warning, f.Severity);
    }

    [Fact]
    public async Task Active_with_no_end_date_has_no_finding()
    {
        var app = App("1", "AAAA", Cert("AAAA", null));

        Assert.Empty(await RunAsync(app));
    }

    [Fact]
    public async Task Active_fine_with_old_inactive_expired_is_info()
    {
        var app = App(
            "1",
            "BBBB",
            Cert("AAAA", Now.AddDays(-400), "CN=old"),
            Cert("BBBB", Now.AddDays(700), "CN=current"));

        var f = Assert.Single(await RunAsync(app));

        Assert.Equal(Severity.Info, f.Severity);
        Assert.Equal("Inactive signing certificate expired 400 days ago; consider removing it.", f.Message);
        Assert.Equal("CN=old", f.SubObjectDisplayName);
        Assert.Equal(Now.AddDays(-400), f.DueDate);
    }

    [Fact]
    public async Task Inactive_expiring_soon_has_no_finding()
    {
        var app = App("1", "BBBB", Cert("AAAA", Now.AddDays(3)), Cert("BBBB", Now.AddDays(700)));

        Assert.Empty(await RunAsync(app));
    }

    [Fact]
    public async Task Newer_inactive_certificate_is_mentioned_and_severity_stays()
    {
        var app = App("1", "AAAA", Cert("AAAA", Now.AddDays(3)), Cert("BBBB", Now.AddDays(1000)));

        var f = Assert.Single(await RunAsync(app));

        Assert.Equal(Severity.Critical, f.Severity);
        Assert.Equal(
            "Active signing certificate expires in 3 days (within critical window). A newer certificate exists but " +
            "is not active. Make it active and give it to the service provider.",
            f.Message);
    }

    [Fact]
    public async Task Newer_certificate_hint_is_added_when_active_has_expired()
    {
        var app = App("1", "AAAA", Cert("AAAA", Now.AddDays(-2)), Cert("BBBB", Now.AddDays(1000)));

        var f = Assert.Single(await RunAsync(app));

        Assert.Equal(Severity.Critical, f.Severity);
        Assert.Contains("SAML sign-in to this app may be failing. A newer certificate exists but is not active.", f.Message);
    }

    [Fact]
    public async Task Inactive_certificate_ending_sooner_is_not_newer()
    {
        var app = App("1", "AAAA", Cert("AAAA", Now.AddDays(20)), Cert("BBBB", Now.AddDays(10)));

        var f = Assert.Single(await RunAsync(app));

        Assert.Equal(Severity.Warning, f.Severity);
        Assert.DoesNotContain("newer certificate", f.Message);
    }

    [Fact]
    public async Task No_signing_certificates_is_warning()
    {
        var app = App("1", "AAAA");

        var f = Assert.Single(await RunAsync(app));

        Assert.Equal(Severity.Warning, f.Severity);
        Assert.Equal("SAML app has no active signing certificate.", f.Message);
        Assert.Null(f.SubObjectType);
        Assert.Null(f.SubObjectDisplayName);
        Assert.Null(f.DueDate);
    }

    [Fact]
    public async Task No_certificate_matching_the_preferred_thumbprint_is_warning()
    {
        var app = App("1", "CCCC", Cert("AAAA", Now.AddDays(500)));

        var f = Assert.Single(await RunAsync(app));

        Assert.Equal(Severity.Warning, f.Severity);
        Assert.Equal("SAML app has no active signing certificate.", f.Message);
    }

    [Fact]
    public async Task No_preferred_thumbprint_is_warning_and_expired_certificates_are_inactive()
    {
        var app = App("1", null, Cert("AAAA", Now.AddDays(-10)));

        var findings = await RunAsync(app);

        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, f => f.Severity == Severity.Warning && f.SubObjectDisplayName is null);
        Assert.Contains(findings, f => f.Severity == Severity.Info && f.Message.StartsWith("Inactive signing certificate expired 10 days ago"));
    }

    [Fact]
    public async Task Thumbprint_match_ignores_case()
    {
        var app = App("1", "abcd", Cert("ABCD", Now.AddDays(3)));

        var f = Assert.Single(await RunAsync(app));

        Assert.StartsWith("Active signing certificate", f.Message);
    }

    [Fact]
    public async Task Certificate_without_display_name_uses_thumbprint()
    {
        var app = App("1", "ABCD", Cert("ABCD", Now.AddDays(3), displayName: ""));

        var f = Assert.Single(await RunAsync(app));

        Assert.Equal("ABCD", f.SubObjectDisplayName);
    }

    [Fact]
    public async Task Disabled_app_findings_are_info_and_say_so()
    {
        var app = App(
            "1",
            "AAAA",
            "saml",
            enabled: false,
            Cert("AAAA", Now.AddDays(-5)),
            Cert("OLD1", Now.AddDays(-400)));

        var findings = await RunAsync(app);

        Assert.Equal(2, findings.Count);
        Assert.All(findings, f => Assert.Equal(Severity.Info, f.Severity));
        Assert.All(findings, f => Assert.StartsWith("App is disabled. ", f.Message));
        Assert.Contains(findings, f => f.Message == "App is disabled. Active signing certificate expired 5 days ago. SAML sign-in to this app may be failing.");
    }

    [Fact]
    public async Task Disabled_app_with_no_active_certificate_is_info()
    {
        var app = App("1", null, "saml", enabled: false);

        var f = Assert.Single(await RunAsync(app));

        Assert.Equal(Severity.Info, f.Severity);
        Assert.Equal("App is disabled. SAML app has no active signing certificate.", f.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("oidc")]
    [InlineData("password")]
    [InlineData("notSupported")]
    public async Task Non_saml_apps_are_ignored(string? mode)
    {
        var app = App("1", "AAAA", mode, enabled: true, Cert("AAAA", Now.AddDays(-5)));

        Assert.Empty(await RunAsync(app));
    }

    [Fact]
    public async Task Saml_mode_is_matched_case_insensitively()
    {
        var app = App("1", "AAAA", "SAML", enabled: true, Cert("AAAA", Now.AddDays(-5)));

        Assert.Single(await RunAsync(app));
    }

    [Fact]
    public async Task Mixed_apps()
    {
        var expired = App("1", "AAAA", Cert("AAAA", Now.AddDays(-1)));
        var warning = App("2", "BBBB", Cert("BBBB", Now.AddDays(20)));
        var fine = App("3", "CCCC", Cert("CCCC", Now.AddDays(300)));
        var oidc = App("4", null, "oidc", enabled: true);
        var noCert = App("5", null);

        var findings = await RunAsync(expired, warning, fine, oidc, noCert);

        Assert.Equal(3, findings.Count);
        Assert.Contains(findings, f => f.ObjectId == "1" && f.Severity == Severity.Critical);
        Assert.Contains(findings, f => f.ObjectId == "2" && f.Severity == Severity.Warning);
        Assert.Contains(findings, f => f.ObjectId == "5" && f.Severity == Severity.Warning);
        Assert.DoesNotContain(findings, f => f.ObjectId is "3" or "4");
    }
}
