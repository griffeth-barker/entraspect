using System;
using System.Collections.Generic;
using System.Linq;
using Entraspect.Core.Checks;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Entraspect.Core.Tests;

public sealed class AppCredentialExpiryCheckTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);

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

    private static async Task<IReadOnlyList<Finding>> RunAsync(
        FakeTimeProvider time,
        CheckOptions options,
        params AppRegistration[] apps)
    {
        var reader = new FakeDirectoryReader(apps);
        var check = new AppCredentialExpiryCheck(time);
        return await check.RunAsync(reader, options);
    }

    [Fact]
    public async Task Expired_with_no_replacement_is_critical()
    {
        var time = new FakeTimeProvider(Now);
        var app = App("1", Cred("secret", AppCredentialType.Secret, Now.AddDays(-5)));

        var findings = await RunAsync(time, new CheckOptions(), app);

        var f = Assert.Single(findings);
        Assert.Equal(Severity.Critical, f.Severity);
        Assert.Equal("Application", f.ObjectType);
        Assert.Equal("1", f.ObjectId);
        Assert.Contains("secret", f.Message);
        Assert.Contains("Secret", f.Message);
        Assert.Contains("expired 5 days ago", f.Message);
        Assert.Equal(Now.AddDays(-5), f.DueDate);
    }

    [Fact]
    public async Task Expired_with_valid_same_type_replacement_is_info()
    {
        var time = new FakeTimeProvider(Now);
        var app = App(
            "1",
            Cred("old", AppCredentialType.Secret, Now.AddDays(-5)),
            Cred("new", AppCredentialType.Secret, Now.AddDays(60)));

        var findings = await RunAsync(time, new CheckOptions(), app);

        var f = Assert.Single(findings);
        Assert.Equal(Severity.Info, f.Severity);
        Assert.Contains("newer credential exists", f.Message);
    }

    [Fact]
    public async Task Expired_secret_with_only_valid_certificate_is_critical()
    {
        var time = new FakeTimeProvider(Now);
        var app = App(
            "1",
            Cred("old", AppCredentialType.Secret, Now.AddDays(-5)),
            Cred("cert", AppCredentialType.Certificate, Now.AddDays(60)));

        var findings = await RunAsync(time, new CheckOptions(), app);

        var f = Assert.Single(findings);
        Assert.Equal(Severity.Critical, f.Severity);
        Assert.Contains("Secret", f.Message);
        Assert.Contains("expired 5 days ago", f.Message);
    }

    [Fact]
    public async Task Expires_within_critical_window_is_critical()
    {
        var time = new FakeTimeProvider(Now);
        var app = App("1", Cred("secret", AppCredentialType.Secret, Now.AddDays(3)));

        var findings = await RunAsync(time, new CheckOptions(), app);

        var f = Assert.Single(findings);
        Assert.Equal(Severity.Critical, f.Severity);
        Assert.Contains("expires in 3 days", f.Message);
    }

    [Fact]
    public async Task Expires_within_warning_window_is_warning()
    {
        var time = new FakeTimeProvider(Now);
        var app = App("1", Cred("secret", AppCredentialType.Secret, Now.AddDays(15)));

        var findings = await RunAsync(time, new CheckOptions(), app);

        var f = Assert.Single(findings);
        Assert.Equal(Severity.Warning, f.Severity);
        Assert.Contains("expires in 15 days", f.Message);
    }

    [Fact]
    public async Task Exactly_on_critical_boundary_is_critical()
    {
        var time = new FakeTimeProvider(Now);
        var app = App("1", Cred("secret", AppCredentialType.Secret, Now.AddDays(7)));

        var findings = await RunAsync(time, new CheckOptions(), app);

        var f = Assert.Single(findings);
        Assert.Equal(Severity.Critical, f.Severity);
    }

    [Fact]
    public async Task Just_above_critical_boundary_is_warning()
    {
        var time = new FakeTimeProvider(Now);
        var app = App("1", Cred("secret", AppCredentialType.Secret, Now.AddDays(8)));

        var findings = await RunAsync(time, new CheckOptions(), app);

        var f = Assert.Single(findings);
        Assert.Equal(Severity.Warning, f.Severity);
    }

    [Fact]
    public async Task Exactly_on_warning_boundary_is_warning()
    {
        var time = new FakeTimeProvider(Now);
        var app = App("1", Cred("secret", AppCredentialType.Secret, Now.AddDays(30)));

        var findings = await RunAsync(time, new CheckOptions(), app);

        var f = Assert.Single(findings);
        Assert.Equal(Severity.Warning, f.Severity);
    }

    [Fact]
    public async Task Beyond_warning_window_has_no_finding()
    {
        var time = new FakeTimeProvider(Now);
        var app = App("1", Cred("secret", AppCredentialType.Secret, Now.AddDays(31)));

        var findings = await RunAsync(time, new CheckOptions(), app);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task No_end_date_has_no_finding()
    {
        var time = new FakeTimeProvider(Now);
        var app = App("1", Cred("secret", AppCredentialType.Secret, null));

        var findings = await RunAsync(time, new CheckOptions(), app);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task Multiple_apps_with_mixed_results()
    {
        var time = new FakeTimeProvider(Now);
        var app1 = App("1", Cred("expired", AppCredentialType.Secret, Now.AddDays(-1)));
        var app2 = App("2", Cred("warn", AppCredentialType.Certificate, Now.AddDays(20)));
        var app3 = App("3", Cred("ok", AppCredentialType.Secret, Now.AddDays(120)));

        var findings = await RunAsync(time, new CheckOptions(), app1, app2, app3);

        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, f => f.ObjectId == "1" && f.Severity == Severity.Critical);
        Assert.Contains(findings, f => f.ObjectId == "2" && f.Severity == Severity.Warning);
        Assert.DoesNotContain(findings, f => f.ObjectId == "3");
    }

    [Fact]
    public async Task Custom_warning_and_critical_days_are_honored()
    {
        var time = new FakeTimeProvider(Now);
        var options = new CheckOptions { ExpiryWarningDays = 10, ExpiryCriticalDays = 3 };
        var app = App("1", Cred("secret", AppCredentialType.Secret, Now.AddDays(5)));

        var findings = await RunAsync(time, options, app);

        var f = Assert.Single(findings);
        Assert.Equal(Severity.Warning, f.Severity);
    }
}
