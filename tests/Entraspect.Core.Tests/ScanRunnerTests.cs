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
    public async Task Cancellation_cancels_running_and_not_yet_run_checks()
    {
        var cancelled = new FakeCheck("c1", run: (_, _, _) => throw new OperationCanceledException());
        var checks = new ICheck[] { cancelled, new FakeCheck("c2"), new FakeCheck("c3") };
        var runner = new ScanRunner(new FakeDirectoryReader(System.Array.Empty<AppRegistration>()), checks, new[] { "Application.Read.All" }, Options, _time);

        var results = await runner.RunAsync();

        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.Equal(CheckResultStatus.Cancelled, r.Status));
    }

    [Fact]
    public async Task Reports_progress_for_each_run_check()
    {
        var messages = new List<string>();
        var progress = new Progress<ScanProgress>(p => messages.Add(p.Message));
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

        // c2 is missing permissions, so progress is only reported for c1 and c3.
        Assert.Equal(new[] { "Running check 1 of 3: First", "Running check 2 of 3: Third" }, messages);
    }

    [Fact]
    public async Task Sets_completed_at_from_time_provider()
    {
        var checks = new ICheck[] { new FakeCheck("c1") };
        var runner = new ScanRunner(new FakeDirectoryReader(System.Array.Empty<AppRegistration>()), checks, new[] { "Application.Read.All" }, Options, _time);

        var results = await runner.RunAsync();

        Assert.Equal(_time.GetUtcNow(), results[0].CompletedAt);
    }
}
