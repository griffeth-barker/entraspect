using System.Collections.Generic;
using System.Linq;

namespace Entraspect.Core;

/// <summary>
/// Builds a <see cref="ConditionalAccessUsage"/> one sign-in at a time, so a reader can count each page of sign-ins
/// and drop it before reading the next, never holding every sign-in in memory.
/// </summary>
public sealed class ConditionalAccessUsageAggregator
{
    private readonly DateTimeOffset _start;
    private readonly DateTimeOffset _end;
    private readonly Dictionary<string, Dictionary<ConditionalAccessResult, int>> _results =
        new(StringComparer.OrdinalIgnoreCase);

    public ConditionalAccessUsageAggregator(DateTimeOffset start, DateTimeOffset end)
    {
        _start = start;
        _end = end;
    }

    public int SignInsRead { get; private set; }

    public int SignInsWithPolicyData { get; private set; }

    /// <summary>
    /// Adds one sign-in's applied policy results. An empty list still counts as a sign-in read, without policy data.
    /// Entries without a policy ID are skipped.
    /// </summary>
    public void Add(IEnumerable<AppliedPolicyResult> appliedPolicies)
    {
        SignInsRead++;
        var hasData = false;

        foreach (var applied in appliedPolicies)
        {
            if (string.IsNullOrWhiteSpace(applied.PolicyId))
            {
                continue;
            }

            hasData = true;
            if (!_results.TryGetValue(applied.PolicyId, out var counts))
            {
                counts = [];
                _results[applied.PolicyId] = counts;
            }

            counts[applied.Result] = counts.GetValueOrDefault(applied.Result) + 1;
        }

        if (hasData)
        {
            SignInsWithPolicyData++;
        }
    }

    public ConditionalAccessUsage ToUsage() =>
        new()
        {
            Start = _start,
            End = _end,
            SignInsRead = SignInsRead,
            SignInsWithPolicyData = SignInsWithPolicyData,
            ResultsByPolicy = _results.ToDictionary(
                p => p.Key,
                p => (IReadOnlyDictionary<ConditionalAccessResult, int>)new Dictionary<ConditionalAccessResult, int>(p.Value),
                StringComparer.OrdinalIgnoreCase),
        };
}
