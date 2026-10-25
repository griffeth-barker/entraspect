using System.Collections.Generic;
using System.Linq;
using Entraspect.Core.Checks;
using Microsoft.Extensions.Time.Testing;

namespace Entraspect.Core.Tests.Checks;

public sealed class ConditionalAccessUnusedPoliciesCheckTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Since = Now.AddDays(-7);

    private readonly FakeTimeProvider _time = new(Now);

    private static ConditionalAccessPolicy Policy(
        string id,
        ConditionalAccessPolicyState state = ConditionalAccessPolicyState.Enabled,
        DateTimeOffset? created = null,
        bool noCreatedDate = false,
        bool servicePrincipals = false) =>
        new()
        {
            Id = id,
            DisplayName = $"Policy {id}",
            State = state,
            CreatedDateTime = noCreatedDate ? null : created ?? Now.AddYears(-1),
            IncludeUsers = ["All"],
            IncludeApplications = ["All"],
            TargetsServicePrincipals = servicePrincipals,
        };

    /// <summary>
    /// Usage over the default period from sign-ins given as their applied policies, for example
    /// <c>[("p1", Success)]</c>; an empty sign-in has no policy data.
    /// </summary>
    private static ConditionalAccessUsage Usage(params (string PolicyId, ConditionalAccessResult Result)[][] signIns)
    {
        var aggregator = new ConditionalAccessUsageAggregator(Since, Now);
        foreach (var signIn in signIns)
        {
            aggregator.Add(signIn.Select(a => new AppliedPolicyResult(a.PolicyId, a.Result)));
        }

        return aggregator.ToUsage();
    }

    /// <summary>Three sign-ins: p1 applied in one, p2 never applied, rp1 report-only applied, rp2 never applied.</summary>
    private static ConditionalAccessUsage ThreeSignIns() =>
        Usage(
            [
                ("p1", ConditionalAccessResult.Success), ("p2", ConditionalAccessResult.NotApplied),
                ("rp1", ConditionalAccessResult.ReportOnlySuccess), ("rp2", ConditionalAccessResult.ReportOnlyNotApplied),
            ],
            [
                ("p1", ConditionalAccessResult.NotApplied), ("p2", ConditionalAccessResult.NotApplied),
                ("rp1", ConditionalAccessResult.ReportOnlyNotApplied), ("rp2", ConditionalAccessResult.ReportOnlyNotApplied),
            ],
            []);

    private static FakeDirectoryReader Reader(ConditionalAccessUsage usage, params ConditionalAccessPolicy[] policies) =>
        new([], policies: policies) { Usage = usage };

    private Task<IReadOnlyList<Finding>> RunAsync(FakeDirectoryReader reader, CheckOptions? options = null) =>
        new ConditionalAccessUnusedPoliciesCheck(_time).RunAsync(reader, options ?? new CheckOptions());

    [Fact]
    public async Task Enabled_policy_that_never_applied_is_a_warning()
    {
        var findings = await RunAsync(Reader(ThreeSignIns(), Policy("p2")));

        var finding = Assert.Single(findings);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("ca-unused-policies", finding.CheckId);
        Assert.Equal("ConditionalAccessPolicy", finding.ObjectType);
        Assert.Equal("p2", finding.ObjectId);
        Assert.Equal("Policy p2", finding.ObjectDisplayName);
        Assert.Equal("Didn't apply to any interactive sign-in in the last 7 days (3 sign-ins checked).", finding.Message);
        Assert.Null(finding.SubObjectType);
        Assert.Null(finding.SubObjectDisplayName);
        Assert.Null(finding.DueDate);
    }

    [Fact]
    public async Task Enabled_policy_that_applied_has_no_finding()
    {
        var findings = await RunAsync(Reader(ThreeSignIns(), Policy("p1")));

        Assert.Empty(findings);
    }

    [Fact]
    public async Task Enabled_policy_that_only_blocked_sign_ins_counts_as_applied()
    {
        var usage = Usage([("p1", ConditionalAccessResult.Failure)]);

        Assert.Empty(await RunAsync(Reader(usage, Policy("p1"))));
    }

    [Theory]
    [InlineData(ConditionalAccessResult.NotApplied)]
    [InlineData(ConditionalAccessResult.NotEnabled)]
    [InlineData(ConditionalAccessResult.ReportOnlySuccess)]
    [InlineData(ConditionalAccessResult.Unknown)]
    public async Task Enabled_policy_with_only_other_results_is_unused(ConditionalAccessResult result)
    {
        var usage = Usage([("p1", result)]);

        Assert.Equal(Severity.Warning, Assert.Single(await RunAsync(Reader(usage, Policy("p1")))).Severity);
    }

    [Fact]
    public async Task Enabled_policy_that_no_sign_in_listed_is_unused()
    {
        var usage = Usage([("other", ConditionalAccessResult.Success)]);

        Assert.Equal("p1", Assert.Single(await RunAsync(Reader(usage, Policy("p1")))).ObjectId);
    }

    [Fact]
    public async Task Report_only_policy_that_never_applied_is_info()
    {
        var findings = await RunAsync(Reader(ThreeSignIns(), Policy("rp2", ConditionalAccessPolicyState.ReportOnly)));

        var finding = Assert.Single(findings);
        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Equal("rp2", finding.ObjectId);
        Assert.Equal("Didn't apply to any interactive sign-in in the last 7 days (3 sign-ins checked).", finding.Message);
    }

    [Theory]
    [InlineData(ConditionalAccessResult.ReportOnlySuccess)]
    [InlineData(ConditionalAccessResult.ReportOnlyFailure)]
    [InlineData(ConditionalAccessResult.ReportOnlyInterrupted)]
    public async Task Report_only_policy_that_applied_has_no_finding(ConditionalAccessResult result)
    {
        var usage = Usage([("rp1", result)]);

        Assert.Empty(await RunAsync(Reader(usage, Policy("rp1", ConditionalAccessPolicyState.ReportOnly))));
    }

    [Theory]
    [InlineData(ConditionalAccessResult.ReportOnlyNotApplied)]
    [InlineData(ConditionalAccessResult.Success)]
    public async Task Report_only_policy_with_only_other_results_is_unused(ConditionalAccessResult result)
    {
        var usage = Usage([("rp1", result)]);

        var finding = Assert.Single(await RunAsync(Reader(usage, Policy("rp1", ConditionalAccessPolicyState.ReportOnly))));
        Assert.Equal(Severity.Info, finding.Severity);
    }

    [Fact]
    public async Task Reports_every_unused_policy_and_only_those()
    {
        var findings = await RunAsync(Reader(
            ThreeSignIns(),
            Policy("p1"),
            Policy("p2"),
            Policy("rp1", ConditionalAccessPolicyState.ReportOnly),
            Policy("rp2", ConditionalAccessPolicyState.ReportOnly)));

        Assert.Equal(new[] { "p2", "rp2" }, findings.Select(f => f.ObjectId));
    }

    [Fact]
    public async Task No_sign_ins_fails_the_check()
    {
        var reader = Reader(Usage(), Policy("p1"));

        var error = await Assert.ThrowsAsync<DirectoryReadException>(() => RunAsync(reader));

        Assert.Equal("No interactive sign-ins found in the last 7 days.", error.Message);
        Assert.Null(error.StatusCode);
    }

    [Fact]
    public async Task Sign_ins_without_conditional_access_data_fail_the_check_instead_of_reporting_every_policy()
    {
        var reader = Reader(Usage([], []), Policy("p1"), Policy("rp1", ConditionalAccessPolicyState.ReportOnly));

        var error = await Assert.ThrowsAsync<DirectoryReadException>(() => RunAsync(reader));

        Assert.Equal(
            "Sign-in logs don't include Conditional Access data. Check that Policy.Read.All is granted, then reconnect.",
            error.Message);
    }

    [Fact]
    public async Task Policy_created_during_the_period_is_skipped()
    {
        var reader = Reader(
            ThreeSignIns(),
            Policy("p2", created: Now.AddDays(-2)),
            Policy("rp2", ConditionalAccessPolicyState.ReportOnly, created: Since));

        Assert.Empty(await RunAsync(reader));
    }

    [Fact]
    public async Task Policy_created_before_the_period_or_without_a_created_date_is_checked()
    {
        var reader = Reader(
            ThreeSignIns(),
            Policy("p2", created: Since.AddSeconds(-1)),
            Policy("rp2", ConditionalAccessPolicyState.ReportOnly, noCreatedDate: true));

        Assert.Equal(new[] { "p2", "rp2" }, (await RunAsync(reader)).Select(f => f.ObjectId));
    }

    [Fact]
    public async Task Policy_that_targets_service_principals_is_skipped()
    {
        var reader = Reader(ThreeSignIns(), Policy("p2", servicePrincipals: true));

        Assert.Empty(await RunAsync(reader));
    }

    [Fact]
    public async Task Disabled_policy_is_skipped()
    {
        var reader = Reader(ThreeSignIns(), Policy("p2", ConditionalAccessPolicyState.Disabled));

        Assert.Empty(await RunAsync(reader));
    }

    [Fact]
    public async Task Does_not_read_sign_ins_when_no_policy_is_in_scope()
    {
        var reader = Reader(
            ThreeSignIns(),
            Policy("p2", ConditionalAccessPolicyState.Disabled),
            Policy("p3", servicePrincipals: true));

        Assert.Empty(await RunAsync(reader));
        Assert.Empty(reader.UsageReads);
    }

    [Fact]
    public async Task Reads_sign_ins_since_the_lookback_days()
    {
        var reader = Reader(ThreeSignIns(), Policy("p1"));

        await RunAsync(reader, new CheckOptions { SignInLookbackDays = 14 });

        Assert.Equal(new[] { Now.AddDays(-14) }, reader.UsageReads);
    }

    [Fact]
    public async Task Message_uses_the_singular_for_one_day_and_one_sign_in()
    {
        var reader = Reader(Usage([("other", ConditionalAccessResult.Success)]), Policy("p1"));

        var finding = Assert.Single(await RunAsync(reader, new CheckOptions { SignInLookbackDays = 1 }));

        Assert.Equal("Didn't apply to any interactive sign-in in the last day (1 sign-in checked).", finding.Message);
    }

    [Fact]
    public async Task Scan_returns_the_period_and_the_number_of_sign_ins_read()
    {
        var check = new ConditionalAccessUnusedPoliciesCheck(_time);

        var run = await check.ScanAsync(Reader(ThreeSignIns(), Policy("p2")), new CheckOptions(), null, default);

        Assert.Single(run.Findings);
        Assert.Equal(new DataPeriod(Since, Now, 3, "sign-in"), run.DataPeriod);
    }

    [Fact]
    public async Task Reports_how_many_sign_ins_have_been_read()
    {
        var aggregator = new ConditionalAccessUsageAggregator(Since, Now);
        for (var i = 0; i < 1500; i++)
        {
            aggregator.Add([new AppliedPolicyResult("p1", ConditionalAccessResult.Success)]);
        }

        var messages = new List<string>();
        await new ConditionalAccessUnusedPoliciesCheck(_time).RunAsync(
            Reader(aggregator.ToUsage(), Policy("p1")),
            new CheckOptions(),
            new SyncProgress<string>(messages.Add),
            default);

        Assert.Equal(new[] { "reading sign-ins, 1,500 so far" }, messages);
    }

    [Fact]
    public void Is_a_licensed_conditional_access_check_with_read_permissions()
    {
        var check = new ConditionalAccessUnusedPoliciesCheck(_time);

        Assert.Equal("Unused policies", check.Title);
        Assert.Equal(CheckCategory.ConditionalAccess, check.Category);
        Assert.Equal(LicenceRequirement.EntraIdP1, check.RequiredLicence);
        Assert.Equal(new[] { "AuditLog.Read.All", "Policy.Read.All" }, check.RequiredPermissions);
    }

    [Fact]
    public void Columns_are_named_like_the_conditional_access_policy_list()
    {
        var labels = new ConditionalAccessUnusedPoliciesCheck(_time).Labels;

        Assert.Equal("Policy name", labels.ObjectDisplayName);
        Assert.Equal("Policy ID", labels.ObjectId);
        Assert.Null(labels.UserPrincipalName);
        Assert.Null(labels.SubObjectDisplayName);
        Assert.Null(labels.SubObjectType);
        Assert.Null(labels.DueDate);
    }
}
