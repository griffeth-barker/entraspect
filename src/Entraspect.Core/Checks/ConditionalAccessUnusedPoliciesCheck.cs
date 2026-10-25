using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Entraspect.Core.Checks;

public sealed class ConditionalAccessUnusedPoliciesCheck : ICheck
{
    public const string NoPolicyDataMessage =
        "Sign-in logs don't include Conditional Access data. Check that Policy.Read.All is granted, then reconnect.";

    private readonly TimeProvider _timeProvider;

    public ConditionalAccessUnusedPoliciesCheck(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public string Id => "ca-unused-policies";

    public string Title => "Unused policies";

    public string Description =>
        "Surfaces enabled and report-only Conditional Access policies that didn't apply to any interactive sign-in " +
        "in the sign-in lookback days. Non-interactive, service principal and managed identity sign-ins aren't read, " +
        "so policies aimed only at them look unused; policies that target service principals are skipped, as are " +
        "disabled policies and policies created during the period. Every sign-in in the period is read, so this " +
        "check can take a while in a busy tenant.";

    public CheckCategory Category => CheckCategory.ConditionalAccess;

    // AuditLog.Read.All: sign-in logs. Policy.Read.All: the policies, and the applied policies in each sign-in, which
    // Graph leaves out without it.
    public IReadOnlyList<string> RequiredPermissions => new[] { "AuditLog.Read.All", "Policy.Read.All" };

    // Conditional Access and sign-in logs need Entra ID P1 or P2.
    public LicenceRequirement RequiredLicence => LicenceRequirement.EntraIdP1;

    // Named like the Conditional Access policy list in the Entra admin center.
    public FindingLabels Labels { get; } = new()
    {
        ObjectDisplayName = "Policy name",
        ObjectId = "Policy ID",
        SubObjectDisplayName = null,
        SubObjectType = null,
        DueDate = null,
    };

    public Task<IReadOnlyList<Finding>> RunAsync(
        IDirectoryReader reader,
        CheckOptions options,
        CancellationToken cancellationToken = default) =>
        RunAsync(reader, options, null, cancellationToken);

    public async Task<IReadOnlyList<Finding>> RunAsync(
        IDirectoryReader reader,
        CheckOptions options,
        IProgress<string>? progress,
        CancellationToken cancellationToken) =>
        (await ScanAsync(reader, options, progress, cancellationToken).ConfigureAwait(false)).Findings;

    public async Task<CheckRun> ScanAsync(
        IDirectoryReader reader,
        CheckOptions options,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var days = options.SignInLookbackDays;
        var since = _timeProvider.GetUtcNow().AddDays(-days);

        var policies = (await reader.GetConditionalAccessPoliciesAsync(cancellationToken).ConfigureAwait(false))
            .Where(p => IsInScope(p, since))
            .ToList();

        // Nothing to judge, so the sign-in logs, the slowest read in a scan, aren't read.
        if (policies.Count == 0)
        {
            return new CheckRun([]);
        }

        var usage = await reader.GetConditionalAccessUsageAsync(
                since,
                progress is null
                    ? null
                    : new SyncProgress<int>(n => progress.Report($"reading sign-ins, {Number(n)} so far")),
                cancellationToken)
            .ConfigureAwait(false);

        if (usage.SignInsRead == 0)
        {
            throw new DirectoryReadException($"No interactive sign-ins found in the last {Days(days)}.");
        }

        // Without Policy.Read.All Graph leaves every sign-in's applied policies empty, which would make every policy
        // look unused.
        if (usage.SignInsWithPolicyData == 0)
        {
            throw new DirectoryReadException(NoPolicyDataMessage);
        }

        var message =
            $"Didn't apply to any interactive sign-in in the last {Days(days)} " +
            $"({Number(usage.SignInsRead)} sign-in{(usage.SignInsRead == 1 ? string.Empty : "s")} checked).";

        var findings = new List<Finding>();
        foreach (var policy in policies)
        {
            if (policy.State == ConditionalAccessPolicyState.Enabled && !Applied(usage, policy.Id))
            {
                findings.Add(MakeFinding(policy, Severity.Warning, message));
            }
            else if (policy.State == ConditionalAccessPolicyState.ReportOnly && !AppliedReportOnly(usage, policy.Id))
            {
                findings.Add(MakeFinding(policy, Severity.Info, message));
            }
        }

        return new CheckRun(findings, new DataPeriod(usage.Start, usage.End, usage.SignInsRead, "sign-in"));
    }

    /// <summary>
    /// Enabled and report-only policies created before the period, that don't target service principals: their sign-ins
    /// aren't read. Disabled policies are covered by Policy hygiene. A policy without a created date is in scope.
    /// </summary>
    private static bool IsInScope(ConditionalAccessPolicy policy, DateTimeOffset since) =>
        policy.State != ConditionalAccessPolicyState.Disabled &&
        !policy.TargetsServicePrincipals &&
        !(policy.CreatedDateTime >= since);

    private static bool Applied(ConditionalAccessUsage usage, string policyId) =>
        usage.Count(policyId, ConditionalAccessResult.Success) > 0 ||
        usage.Count(policyId, ConditionalAccessResult.Failure) > 0;

    private static bool AppliedReportOnly(ConditionalAccessUsage usage, string policyId) =>
        usage.Count(policyId, ConditionalAccessResult.ReportOnlySuccess) > 0 ||
        usage.Count(policyId, ConditionalAccessResult.ReportOnlyFailure) > 0 ||
        usage.Count(policyId, ConditionalAccessResult.ReportOnlyInterrupted) > 0;

    private static string Days(int days) => days == 1 ? "day" : $"{days} days";

    private static string Number(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private Finding MakeFinding(ConditionalAccessPolicy policy, Severity severity, string message) =>
        new()
        {
            CheckId = Id,
            Severity = severity,
            ObjectType = ConditionalAccessPolicyHygieneCheck.ObjectType,
            ObjectId = policy.Id,
            ObjectDisplayName = policy.DisplayName,
            Message = message,
        };
}
