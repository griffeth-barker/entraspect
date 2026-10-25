using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;

namespace Entraspect.Core.Tests;

public sealed class ScanRunnerTests
{
    private readonly FakeTimeProvider _time = new(new System.DateTimeOffset(2026, 1, 15, 0, 0, 0, System.TimeSpan.Zero));

    private static readonly CheckOptions Options = new();

    [Fact]
    public async Task Runs_checks_in_catalog_order()
    {
        var checks = new ICheck[] { new FakeCheck("c1"), new FakeCheck("c2"), new FakeCheck("c3") };
        var runner = new ScanRunner(new FakeDirectoryReader(System.Array.Empty<AppRegistration>()), checks, new[] { "Application.Read.All" }, Options, _time);

        var results = await runner.RunAsync();

        Assert.Equal(new[] { "c1", "c2", "c3" }, results.Select(r => r.CheckId));
        Assert.All(results, r => Assert.Equal(CheckResultStatus.Succeeded, r.Status));
    }

    [Fact]
    public async Task Missing_permissions_skips_check()
    {
        var checks = new ICheck[] { new FakeCheck("c1"), new FakeCheck("c2", requiredPermissions: new[] { "Foo.Read.All" }) };
        var runner = new ScanRunner(new FakeDirectoryReader(System.Array.Empty<AppRegistration>()), checks, new[] { "Application.Read.All" }, Options, _time);

        var results = await runner.RunAsync();

        Assert.Equal(CheckResultStatus.Succeeded, results[0].Status);
        Assert.Equal(CheckResultStatus.MissingPermissions, results[1].Status);
        Assert.Equal(new[] { "Foo.Read.All" }, results[1].MissingPermissions);
    }

    [Fact]
    public async Task One_failing_check_does_not_stop_the_rest()
    {
        var failing = new FakeCheck("c2", run: (_, _, _) => throw new DirectoryReadException("boom", 403));
        var checks = new ICheck[] { new FakeCheck("c1"), failing, new FakeCheck("c3") };
        var runner = new ScanRunner(new FakeDirectoryReader(System.Array.Empty<AppRegistration>()), checks, new[] { "Application.Read.All" }, Options, _time);

        var results = await runner.RunAsync();

        Assert.Equal(CheckResultStatus.Succeeded, results[0].Status);
        Assert.Equal(CheckResultStatus.Failed, results[1].Status);
        Assert.Equal("boom", results[1].ErrorMessage);
        Assert.Equal(403, results[1].StatusCode);
        Assert.Equal(CheckResultStatus.Succeeded, results[2].Status);
    }

    [Fact]
    public async Task Succeeded_check_reports_when_its_data_was_updated()
    {
        var updated = new System.DateTimeOffset(2026, 1, 14, 6, 0, 0, System.TimeSpan.Zero);
        var users = new[]
        {
            new UserRegistration
            {
                Id = "u1", UserPrincipalName = "u1@contoso.example", DisplayName = "U1", UserType = UserType.Member,
                IsAdmin = false, IsMfaRegistered = false, IsMfaCapable = false, MethodsRegistered = [],
                LastUpdatedDateTime = updated,
            },
        };
        var checks = new ICheck[] { new Core.Checks.UsersMfaNotRegisteredCheck(), new FakeCheck("c2") };
        var runner = new ScanRunner(new FakeDirectoryReader(users), checks, new[] { "AuditLog.Read.All", "Application.Read.All" }, Options, _time);

        var results = await runner.RunAsync();

        Assert.Equal(updated, results[0].DataUpdatedAt);
        Assert.Null(results[1].DataUpdatedAt);
    }

    [Fact]
    public async Task Unlicensed_check_is_skipped_without_reporting_missing_permissions()
    {
        var ran = new List<string>();
        var checks = new ICheck[]
        {
            new FakeCheck("c1"),
            new FakeCheck(
                "c2",
                requiredPermissions: new[] { "AuditLog.Read.All" },
                run: (_, _, _) => { ran.Add("c2"); return Task.FromResult<IReadOnlyList<Finding>>(System.Array.Empty<Finding>()); },
                requiredLicence: LicenceRequirement.EntraIdP1),
        };
        var progress = new List<ScanProgress>();
        var runner = new ScanRunner(
            new FakeDirectoryReader(System.Array.Empty<AppRegistration>()), checks, new[] { "Application.Read.All" },
            Options, _time, new SyncProgress(progress.Add), TenantLicence.Free);

        var results = await runner.RunAsync();

        Assert.Equal(CheckResultStatus.Succeeded, results[0].Status);
        Assert.Equal(CheckResultStatus.MissingLicence, results[1].Status);
        Assert.Empty(results[1].MissingPermissions);
        Assert.Empty(ran);
        Assert.Equal(1, Assert.Single(progress).Total);
    }

    [Theory]
    [InlineData(TenantLicence.Unknown)]
    [InlineData(TenantLicence.EntraIdP1)]
    [InlineData(TenantLicence.EntraIdP2)]
    public async Task Licensed_or_unknown_tenant_runs_a_p1_check(TenantLicence licence)
    {
        var checks = new ICheck[] { new FakeCheck("c1", requiredLicence: LicenceRequirement.EntraIdP1) };
        var runner = new ScanRunner(
            new FakeDirectoryReader(System.Array.Empty<AppRegistration>()), checks, System.Array.Empty<string>(),
            Options, _time, tenantLicence: licence);

        var results = await runner.RunAsync();

        Assert.Equal(CheckResultStatus.Succeeded, results[0].Status);
    }

    [Fact]
    public async Task Cancellation_keeps_missing_licence_for_unlicensed_checks()
    {
        using var cts = new CancellationTokenSource();
        var cancelling = new FakeCheck("c1", run: (_, _, ct) =>
        {
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<Finding>>(System.Array.Empty<Finding>());
        });
        var checks = new ICheck[] { cancelling, new FakeCheck("c2", requiredLicence: LicenceRequirement.EntraIdP2) };
        var runner = new ScanRunner(
            new FakeDirectoryReader(System.Array.Empty<AppRegistration>()), checks, System.Array.Empty<string>(),
            Options, _time, tenantLicence: TenantLicence.EntraIdP1);

        var results = await runner.RunAsync(cts.Token);

        Assert.Equal(
            new[] { CheckResultStatus.Cancelled, CheckResultStatus.MissingLicence },
            results.Select(r => r.Status));
    }

    [Fact]
    public async Task Failed_check_keeps_the_error_code()
    {
        var failing = new FakeCheck(
            "c1",
            run: (_, _, _) => throw new DirectoryReadException("no licence", 403, errorCode: "Some_Code"));
        var runner = new ScanRunner(new FakeDirectoryReader(System.Array.Empty<AppRegistration>()), new ICheck[] { failing }, new[] { "Application.Read.All" }, Options, _time);

        var results = await runner.RunAsync();

        Assert.Equal(CheckResultStatus.Failed, results[0].Status);
        Assert.Equal("Some_Code", results[0].ErrorCode);
    }

    [Fact]
    public async Task Cancellation_cancels_running_and_not_yet_run_checks()
    {
        using var cts = new CancellationTokenSource();
        var cancelling = new FakeCheck("c2", run: (_, _, ct) =>
        {
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<Finding>>(System.Array.Empty<Finding>());
        });
        var checks = new ICheck[] { new FakeCheck("c1"), cancelling, new FakeCheck("c3") };
        var runner = new ScanRunner(new FakeDirectoryReader(System.Array.Empty<AppRegistration>()), checks, new[] { "Application.Read.All" }, Options, _time);

        var results = await runner.RunAsync(cts.Token);

        Assert.Equal(
            new[] { CheckResultStatus.Succeeded, CheckResultStatus.Cancelled, CheckResultStatus.Cancelled },
            results.Select(r => r.Status));
    }

    [Fact]
    public async Task Cancellation_between_checks_stops_before_next_check_runs()
    {
        using var cts = new CancellationTokenSource();
        var ran = new List<string>();
        var first = new FakeCheck("c1", run: (_, _, _) =>
        {
            ran.Add("c1");
            cts.Cancel();
            return Task.FromResult<IReadOnlyList<Finding>>(System.Array.Empty<Finding>());
        });
        var second = new FakeCheck("c2", run: (_, _, _) =>
        {
            ran.Add("c2");
            return Task.FromResult<IReadOnlyList<Finding>>(System.Array.Empty<Finding>());
        });
        var runner = new ScanRunner(new FakeDirectoryReader(System.Array.Empty<AppRegistration>()), new ICheck[] { first, second }, new[] { "Application.Read.All" }, Options, _time);

        var results = await runner.RunAsync(cts.Token);

        Assert.Equal(new[] { "c1" }, ran);
        Assert.Equal(CheckResultStatus.Succeeded, results[0].Status);
        Assert.Equal(CheckResultStatus.Cancelled, results[1].Status);
    }

    [Fact]
    public async Task Cancellation_not_requested_by_caller_fails_only_that_check()
    {
        var timingOut = new FakeCheck("c1", run: (_, _, _) => throw new TaskCanceledException());
        var checks = new ICheck[] { timingOut, new FakeCheck("c2") };
        var runner = new ScanRunner(new FakeDirectoryReader(System.Array.Empty<AppRegistration>()), checks, new[] { "Application.Read.All" }, Options, _time);

        var results = await runner.RunAsync();

        Assert.Equal(CheckResultStatus.Failed, results[0].Status);
        Assert.NotNull(results[0].ErrorMessage);
        Assert.Equal(CheckResultStatus.Succeeded, results[1].Status);
    }

    [Fact]
    public async Task Reports_progress_for_each_run_check()
    {
        var messages = new List<string>();
        var progress = new SyncProgress(p => messages.Add(p.Message));
        var checks = new ICheck[]
        {
            new FakeCheck("c1", title: "First"),
            new FakeCheck("c2", title: "Second", requiredPermissions: new[] { "Foo.Read.All" }),
            new FakeCheck("c3", title: "Third"),
        };
        var runner = new ScanRunner(
            new FakeDirectoryReader(System.Array.Empty<AppRegistration>()),
            checks,
            new[] { "Application.Read.All" },
            Options,
            _time,
            progress);

        await runner.RunAsync();

        // c2 is missing permissions, so it isn't counted and progress is only reported for c1 and c3.
        Assert.Equal(new[] { "Running check 1 of 2: First", "Running check 2 of 2: Third" }, messages);
    }

    [Fact]
    public async Task Sets_completed_at_from_time_provider()
    {
        var checks = new ICheck[] { new FakeCheck("c1") };
        var runner = new ScanRunner(new FakeDirectoryReader(System.Array.Empty<AppRegistration>()), checks, new[] { "Application.Read.All" }, Options, _time);

        var results = await runner.RunAsync();

        Assert.Equal(_time.GetUtcNow(), results[0].CompletedAt);
    }

    /// <summary>Reports synchronously, unlike <see cref="Progress{T}"/>, which posts to the thread pool.</summary>
    private sealed class SyncProgress(Action<ScanProgress> report) : IProgress<ScanProgress>
    {
        public void Report(ScanProgress value) => report(value);
    }
}
