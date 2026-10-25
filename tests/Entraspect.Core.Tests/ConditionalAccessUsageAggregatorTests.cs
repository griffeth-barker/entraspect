using System;

namespace Entraspect.Core.Tests;

public sealed class ConditionalAccessUsageAggregatorTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);

    private static AppliedPolicyResult Applied(string policyId, ConditionalAccessResult result) => new(policyId, result);

    [Fact]
    public void Empty_aggregator_has_the_period_and_no_sign_ins()
    {
        var usage = new ConditionalAccessUsageAggregator(Start, End).ToUsage();

        Assert.Equal(Start, usage.Start);
        Assert.Equal(End, usage.End);
        Assert.Equal(0, usage.SignInsRead);
        Assert.Equal(0, usage.SignInsWithPolicyData);
        Assert.Empty(usage.ResultsByPolicy);
        Assert.Equal(0, usage.Count("p1", ConditionalAccessResult.Success));
    }

    [Fact]
    public void Counts_each_policy_result_per_sign_in()
    {
        var aggregator = new ConditionalAccessUsageAggregator(Start, End);

        aggregator.Add([
            Applied("p1", ConditionalAccessResult.Success),
            Applied("p2", ConditionalAccessResult.NotApplied),
            Applied("p3", ConditionalAccessResult.ReportOnlyInterrupted),
        ]);
        aggregator.Add([
            Applied("p1", ConditionalAccessResult.Failure),
            Applied("p2", ConditionalAccessResult.NotApplied),
            Applied("p3", ConditionalAccessResult.ReportOnlyNotApplied),
        ]);
        aggregator.Add([Applied("P1", ConditionalAccessResult.Success)]);

        var usage = aggregator.ToUsage();

        Assert.Equal(3, usage.SignInsRead);
        Assert.Equal(3, usage.SignInsWithPolicyData);
        Assert.Equal(2, usage.Count("p1", ConditionalAccessResult.Success));
        Assert.Equal(1, usage.Count("p1", ConditionalAccessResult.Failure));
        Assert.Equal(2, usage.Count("p2", ConditionalAccessResult.NotApplied));
        Assert.Equal(0, usage.Count("p2", ConditionalAccessResult.Success));
        Assert.Equal(1, usage.Count("p3", ConditionalAccessResult.ReportOnlyInterrupted));
        Assert.Equal(1, usage.Count("p3", ConditionalAccessResult.ReportOnlyNotApplied));
    }

    [Fact]
    public void Sign_ins_without_applied_policies_count_as_read_without_policy_data()
    {
        var aggregator = new ConditionalAccessUsageAggregator(Start, End);

        aggregator.Add([]);
        aggregator.Add([Applied("", ConditionalAccessResult.Success), Applied(" ", ConditionalAccessResult.Failure)]);
        aggregator.Add([Applied("p1", ConditionalAccessResult.NotEnabled)]);

        var usage = aggregator.ToUsage();

        Assert.Equal(3, usage.SignInsRead);
        Assert.Equal(1, usage.SignInsWithPolicyData);
        Assert.Equal(["p1"], usage.ResultsByPolicy.Keys);
    }

    [Fact]
    public void Keeps_unknown_results_in_their_own_count()
    {
        var aggregator = new ConditionalAccessUsageAggregator(Start, End);

        aggregator.Add([Applied("p1", ConditionalAccessResult.Unknown)]);
        aggregator.Add([Applied("p1", ConditionalAccessResult.Unknown)]);

        var usage = aggregator.ToUsage();

        Assert.Equal(2, usage.Count("p1", ConditionalAccessResult.Unknown));
        Assert.Equal(0, usage.Count("p1", ConditionalAccessResult.Success));
    }

    [Fact]
    public void Usage_is_a_snapshot_that_later_sign_ins_do_not_change()
    {
        var aggregator = new ConditionalAccessUsageAggregator(Start, End);
        aggregator.Add([Applied("p1", ConditionalAccessResult.Success)]);

        var usage = aggregator.ToUsage();
        aggregator.Add([Applied("p1", ConditionalAccessResult.Success)]);

        Assert.Equal(1, usage.SignInsRead);
        Assert.Equal(1, usage.Count("p1", ConditionalAccessResult.Success));
        Assert.Equal(2, aggregator.ToUsage().Count("p1", ConditionalAccessResult.Success));
    }
}
