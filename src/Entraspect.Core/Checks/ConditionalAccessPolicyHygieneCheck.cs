using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Entraspect.Core.Checks;

public sealed class ConditionalAccessPolicyHygieneCheck : ICheck
{
    public const string ObjectType = "ConditionalAccessPolicy";

    // SubObjectType values: what the finding is about.
    public const string DisabledIssue = "Disabled";
    public const string ReportOnlyIssue = "Report-only";
    public const string NoUsersIssue = "No users";
    public const string NoAppsIssue = "No apps";
    public const string EmptyGroupsIssue = "Empty groups";
    public const string DeletedReferenceIssue = "Deleted reference";

    private readonly TimeProvider _timeProvider;

    public ConditionalAccessPolicyHygieneCheck(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public string Id => "ca-policy-hygiene";

    public string Title => "Policy hygiene";

    public string Description =>
        "Surfaces Conditional Access policies that are disabled, have been in report-only mode for at least the " +
        "report-only warning days, include no users or no apps, include only empty groups, or include or exclude " +
        "users or groups that have been deleted. Each user a policy names is looked up on its own, so this check " +
        "can take a while. A deleted user still in the recycle bin counts as deleted.";

    public CheckCategory Category => CheckCategory.ConditionalAccess;

    // Group.Read.All: which groups exist, and whether they have members, which Graph refuses to count with
    // GroupMember.Read.All alone (spec 020). User.Read.All: whether users exist (User.ReadBasic.All is delegated only).
    public IReadOnlyList<string> RequiredPermissions =>
        new[] { "Policy.Read.All", "User.Read.All", "Group.Read.All" };

    // Conditional Access needs Entra ID P1 or P2.
    public LicenceRequirement RequiredLicence => LicenceRequirement.EntraIdP1;

    // Named like the Conditional Access policy list in the Entra admin center.
    public FindingLabels Labels { get; } = new()
    {
        ObjectDisplayName = "Policy name",
        ObjectId = "Policy ID",
        SubObjectDisplayName = "Deleted object ID",
        SubObjectType = "Issue",
        DueDate = "Modified date",
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
        CancellationToken cancellationToken)
    {
        var policies = await reader.GetConditionalAccessPoliciesAsync(cancellationToken).ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow();

        // Disabled policies get only the disabled finding, so their users and groups aren't looked up.
        var active = policies.Where(p => p.State != ConditionalAccessPolicyState.Disabled).ToList();

        var namesGroups = active.Any(p => p.IncludeGroups.Concat(p.ExcludeGroups).Any(IsObjectId));
        var groups = namesGroups
            ? (await reader.GetGroupsAsync(cancellationToken).ConfigureAwait(false))
                .ToDictionary(g => g.Id, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, DirectoryGroup>(StringComparer.OrdinalIgnoreCase);
        var groupCounts = namesGroups
            ? await reader.GetGroupCountsAsync(cancellationToken).ConfigureAwait(false)
            : new Dictionary<string, GroupCounts>(StringComparer.OrdinalIgnoreCase);

        var userIds = active
            .SelectMany(p => p.IncludeUsers.Concat(p.ExcludeUsers))
            .Where(IsObjectId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var missingUsers = userIds.Count == 0
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : (await reader.GetMissingUserIdsAsync(
                    userIds,
                    progress is null ? null : new SyncProgress<int>(n => progress.Report($"looking up user {n} of {userIds.Count}")),
                    cancellationToken)
                .ConfigureAwait(false))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var findings = new List<Finding>();
        foreach (var policy in policies)
        {
            AddFindings(policy, options, now, groups, groupCounts, missingUsers, findings);
        }

        return findings;
    }

    /// <summary>
    /// Whether <paramref name="value"/> in a policy's user or group list is an object ID. Special values such as
    /// <c>All</c>, <c>None</c> and <c>GuestsOrExternalUsers</c> aren't, and neither is anything else that isn't a GUID.
    /// </summary>
    public static bool IsObjectId(string value) => Guid.TryParse(value, out _);

    private void AddFindings(
        ConditionalAccessPolicy policy,
        CheckOptions options,
        DateTimeOffset now,
        IReadOnlyDictionary<string, DirectoryGroup> groups,
        IReadOnlyDictionary<string, GroupCounts> groupCounts,
        IReadOnlySet<string> missingUsers,
        List<Finding> findings)
    {
        if (policy.State == ConditionalAccessPolicyState.Disabled)
        {
            findings.Add(MakeFinding(policy, Severity.Info, DisabledIssue, "Policy is disabled."));
            return;
        }

        if (policy.State == ConditionalAccessPolicyState.ReportOnly &&
            LastChanged(policy) is { } changed)
        {
            var days = (now.UtcDateTime.Date - changed.UtcDateTime.Date).Days;
            if (days >= options.ReportOnlyWarningDays)
            {
                findings.Add(MakeFinding(
                    policy,
                    Severity.Warning,
                    ReportOnlyIssue,
                    $"Policy has been in report-only mode for {Days(days)} and is not enforced."));
            }
        }

        var includesUsersDirectly = !IsEmptyOrNone(policy.IncludeUsers) || policy.IncludeRoles.Any(HasValue) ||
            policy.IncludeGuestsOrExternalUsers;
        var includedGroups = policy.IncludeGroups.Where(HasValue).ToList();

        if (!includesUsersDirectly && includedGroups.Count == 0 && !policy.TargetsServicePrincipals)
        {
            findings.Add(MakeFinding(
                policy,
                Severity.Warning,
                NoUsersIssue,
                "Policy doesn't include any users, so it never applies."));
        }

        if (IsEmptyOrNone(policy.IncludeApplications) && !policy.IncludeApplicationsByFilter &&
            !policy.IncludeUserActions.Any(HasValue) && !policy.IncludeAuthenticationContexts.Any(HasValue))
        {
            findings.Add(MakeFinding(
                policy,
                Severity.Warning,
                NoAppsIssue,
                "Policy doesn't include any apps, so it never applies."));
        }

        // Deleted groups apply to no one either; they get their own findings below. If every included group is
        // deleted, only those are reported. A group missing from the counts (created between the two reads) isn't
        // known to be empty.
        var existingGroups = includedGroups.Where(groups.ContainsKey).ToList();
        if (!includesUsersDirectly && existingGroups.Count > 0 &&
            existingGroups.All(id => groupCounts.TryGetValue(id, out var counts) && !counts.HasMembers))
        {
            findings.Add(MakeFinding(
                policy,
                Severity.Warning,
                EmptyGroupsIssue,
                "Every group this policy includes is empty, so it applies to no one."));
        }

        AddDeleted(policy, policy.IncludeUsers, "user", included: true, id => missingUsers.Contains(id), findings);
        AddDeleted(policy, policy.IncludeGroups, "group", included: true, id => !groups.ContainsKey(id), findings);
        AddDeleted(policy, policy.ExcludeUsers, "user", included: false, id => missingUsers.Contains(id), findings);
        AddDeleted(policy, policy.ExcludeGroups, "group", included: false, id => !groups.ContainsKey(id), findings);
    }

    private void AddDeleted(
        ConditionalAccessPolicy policy,
        IEnumerable<string> ids,
        string kind,
        bool included,
        Func<string, bool> isDeleted,
        List<Finding> findings)
    {
        foreach (var id in ids.Where(IsObjectId).Distinct(StringComparer.OrdinalIgnoreCase).Where(isDeleted))
        {
            findings.Add(MakeFinding(
                policy,
                included ? Severity.Warning : Severity.Info,
                DeletedReferenceIssue,
                included
                    ? $"Policy includes a {kind} that has been deleted: {id}."
                    : $"Policy excludes a {kind} that has been deleted: {id}; remove it from the policy.",
                id));
        }
    }

    /// <summary>When the policy was last changed: its modified date, or its created date if it was never modified.</summary>
    private static DateTimeOffset? LastChanged(ConditionalAccessPolicy policy) =>
        policy.ModifiedDateTime ?? policy.CreatedDateTime;

    private static bool IsEmptyOrNone(IReadOnlyList<string> values) =>
        values.Where(HasValue).All(v => string.Equals(v, "None", StringComparison.OrdinalIgnoreCase));

    private static bool HasValue(string value) => !string.IsNullOrWhiteSpace(value);

    private static string Days(int days) => days == 1 ? "1 day" : $"{days} days";

    private Finding MakeFinding(
        ConditionalAccessPolicy policy,
        Severity severity,
        string issue,
        string message,
        string? deletedObjectId = null) =>
        new()
        {
            CheckId = Id,
            Severity = severity,
            ObjectType = ObjectType,
            ObjectId = policy.Id,
            ObjectDisplayName = policy.DisplayName,
            Message = message,
            // The date goes in its own column so it is shown in the UTC or local time chosen in Settings.
            DueDate = LastChanged(policy),
            SubObjectType = issue,
            SubObjectDisplayName = deletedObjectId,
        };
}
