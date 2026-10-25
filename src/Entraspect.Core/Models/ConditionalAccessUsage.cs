namespace Entraspect.Core;

/// <summary>
/// The result a Conditional Access policy had in one sign-in, as the sign-in logs name it. A value Graph adds later,
/// or one that is missing, is <see cref="Unknown"/>.
/// </summary>
public enum ConditionalAccessResult
{
    Success,
    Failure,
    NotApplied,
    NotEnabled,
    ReportOnlySuccess,
    ReportOnlyFailure,
    ReportOnlyNotApplied,
    ReportOnlyInterrupted,
    Unknown,
}

/// <summary>One entry of a sign-in's applied Conditional Access policies: the policy ID and its result.</summary>
public sealed record AppliedPolicyResult(string PolicyId, ConditionalAccessResult Result);

/// <summary>
/// How Conditional Access policies applied to the interactive sign-ins in a period: how many sign-ins were read, how
/// many of them included applied policy data, and for each policy ID the number of sign-ins per result. Built by
/// <see cref="ConditionalAccessUsageAggregator"/>.
/// </summary>
public sealed class ConditionalAccessUsage
{
    /// <summary>The start of the period read: the earliest sign-in time asked for.</summary>
    public required DateTimeOffset Start { get; init; }

    /// <summary>The end of the period read: when the read began.</summary>
    public required DateTimeOffset End { get; init; }

    public required int SignInsRead { get; init; }

    /// <summary>
    /// Sign-ins that listed at least one applied policy. Graph leaves the list empty when it can't be read (without
    /// Policy.Read.All), and for sign-ins that failed before Conditional Access was evaluated.
    /// </summary>
    public required int SignInsWithPolicyData { get; init; }

    /// <summary>Sign-in counts per result, by policy ID. A policy that no sign-in listed isn't in it.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyDictionary<ConditionalAccessResult, int>> ResultsByPolicy
    {
        get;
        init;
    }

    /// <summary>The number of sign-ins in which the policy had <paramref name="result"/>; 0 if none did.</summary>
    public int Count(string policyId, ConditionalAccessResult result) =>
        ResultsByPolicy.TryGetValue(policyId, out var results) && results.TryGetValue(result, out var count)
            ? count
            : 0;
}
