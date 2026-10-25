using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Entraspect.Core.Checks;

public sealed class UsersMfaNotRequiredCheck : ICheck
{
    public const string SecurityDefaultsMessage =
        "Security defaults are on. All users must register for MFA and are asked for it when Microsoft considers it " +
        "necessary; admins are asked at every sign-in.";

    public const string PerUserMfaMessage =
        "MFA is required by legacy per-user MFA, not Conditional Access. Microsoft recommends moving to Conditional " +
        "Access.";

    public const string NotRequiredMessage = "No enforced Conditional Access policy requires MFA for this user.";

    public const string PerUserMfaUnknownNote = " Per-user MFA state couldn't be read.";

    private readonly TimeProvider _timeProvider;

    public UsersMfaNotRequiredCheck(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public string Id => "users-mfa-not-required";

    public string Title => "MFA not required";

    public string Description =>
        "Answers \"who isn't required to use MFA\": enabled users, including users synced from on-premises, whom no " +
        "enforced Conditional Access policy requires to use MFA for every app. Whether users are registered for MFA " +
        "is a separate question. Admins are Critical, members Warning and guests Info, one level lower when MFA is " +
        "required only in some cases. Users with legacy per-user MFA are Info. Recent single-factor sign-ins are " +
        "shown as evidence. Per-user MFA state and sign-in data come from beta Microsoft Graph, which can change " +
        "without notice. Per-user MFA state is read for each user on its own, so this check can take a while.";

    public CheckCategory Category => CheckCategory.Users;

    // Policy.Read.All: policies, security defaults and per-user MFA state. User.Read.All: every user.
    // GroupMember.Read.All: members of the groups policies and role assignments name. RoleManagement.Read.Directory:
    // active role assignments. AuditLog.Read.All: sign-ins.
    public IReadOnlyList<string> RequiredPermissions =>
        new[]
        {
            "Policy.Read.All", "User.Read.All", "GroupMember.Read.All", "RoleManagement.Read.Directory",
            "AuditLog.Read.All",
        };

    // Conditional Access and sign-in logs need Entra ID P1 or P2.
    public LicenceRequirement RequiredLicence => LicenceRequirement.EntraIdP1;

    // Named like the user list in the Entra admin center. The date column holds the most recent single-factor sign-in.
    public FindingLabels Labels { get; } = new()
    {
        ObjectDisplayName = "Display name",
        UserPrincipalName = "User principal name",
        ObjectId = "Object ID",
        SubObjectDisplayName = null,
        SubObjectType = null,
        DueDate = "Last single-factor sign-in",
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
        // Conditional Access can't be used while security defaults are on.
        if (await reader.GetSecurityDefaultsEnabledAsync(cancellationToken).ConfigureAwait(false))
        {
            return [SecurityDefaultsFinding()];
        }

        var policies = (await reader.GetConditionalAccessPoliciesAsync(cancellationToken).ConfigureAwait(false))
            .Where(MfaRequirementEvaluator.IsRelevant)
            .ToList();
        var users = (await reader.GetUsersAsync(cancellationToken).ConfigureAwait(false))
            .Where(u => u.AccountEnabled)
            .ToList();
        var assignments = await reader.GetDirectoryRoleAssignmentsAsync(cancellationToken).ConfigureAwait(false);

        var userIds = users.Select(u => u.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var groupMembers = await ReadGroupMembersAsync(reader, policies, assignments, userIds, cancellationToken)
            .ConfigureAwait(false);
        var userGroups = Invert(groupMembers);
        var userRoles = UserRoles(assignments, groupMembers);

        var uncovered = new List<(DirectoryUser User, MfaRequirement Requirement, bool IsAdmin)>();
        foreach (var user in users)
        {
            var roles = userRoles.GetValueOrDefault(user.Id) ?? Empty;
            var requirement = MfaRequirementEvaluator.Evaluate(
                policies,
                user,
                userGroups.GetValueOrDefault(user.Id) ?? Empty,
                roles);

            if (requirement.FullyRequiredBy.Count == 0)
            {
                uncovered.Add((user, requirement, roles.Count > 0));
            }
        }

        var findings = new List<Finding>();
        var pending = new List<(DirectoryUser User, Severity Severity, string Message)>();
        var done = 0;

        foreach (var (user, requirement, isAdmin) in uncovered)
        {
            var state = await reader.GetPerUserMfaStateAsync(user.Id, cancellationToken).ConfigureAwait(false);
            progress?.Report($"reading per-user MFA state {++done} of {uncovered.Count}");

            if (state is PerUserMfaState.Enabled or PerUserMfaState.Enforced)
            {
                findings.Add(MakeFinding(user, Severity.Info, PerUserMfaMessage, null));
                continue;
            }

            var (severity, message) = Describe(user, requirement, isAdmin);
            if (state == PerUserMfaState.Unknown)
            {
                message += PerUserMfaUnknownNote;
            }

            pending.Add((user, severity, message));
        }

        if (pending.Count > 0)
        {
            var days = options.SignInLookbackDays;
            var since = _timeProvider.GetUtcNow().AddDays(-days);
            var signIns = await reader.GetSingleFactorSignInsAsync(since, cancellationToken).ConfigureAwait(false);

            foreach (var (user, severity, message) in pending)
            {
                var evidence = signIns.GetValueOrDefault(user.Id);
                findings.Add(MakeFinding(
                    user,
                    severity,
                    evidence is null ? message : message + SignInNote(evidence, days),
                    evidence?.LastSignIn));
            }
        }

        return findings;
    }

    private static readonly IReadOnlySet<string> Empty = new HashSet<string>();

    /// <summary>
    /// The user members of every group the MFA policies include or exclude, and of every role assignment principal that
    /// isn't a user (role-assignable groups; other principals have no members).
    /// </summary>
    private static async Task<Dictionary<string, IReadOnlyList<string>>> ReadGroupMembersAsync(
        IDirectoryReader reader,
        IEnumerable<ConditionalAccessPolicy> policies,
        IEnumerable<DirectoryRoleAssignment> assignments,
        IReadOnlySet<string> userIds,
        CancellationToken cancellationToken)
    {
        var groupIds = policies
            .SelectMany(p => p.IncludeGroups.Concat(p.ExcludeGroups))
            .Concat(assignments.Select(a => a.PrincipalId).Where(id => !userIds.Contains(id)))
            .Where(ConditionalAccessPolicyHygieneCheck.IsObjectId)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var groupId in groupIds)
        {
            result[groupId] = await reader.GetGroupTransitiveUserIdsAsync(groupId, cancellationToken)
                .ConfigureAwait(false);
        }

        return result;
    }

    private static Dictionary<string, IReadOnlySet<string>> Invert(
        IReadOnlyDictionary<string, IReadOnlyList<string>> groupMembers)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (groupId, members) in groupMembers)
        {
            foreach (var userId in members)
            {
                Add(result, userId, groupId);
            }
        }

        return result.ToDictionary(p => p.Key, p => (IReadOnlySet<string>)p.Value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The roles each user holds, directly or through a role-assignable group.</summary>
    private static Dictionary<string, IReadOnlySet<string>> UserRoles(
        IEnumerable<DirectoryRoleAssignment> assignments,
        IReadOnlyDictionary<string, IReadOnlyList<string>> groupMembers)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var assignment in assignments)
        {
            Add(result, assignment.PrincipalId, assignment.RoleDefinitionId);
            foreach (var userId in groupMembers.GetValueOrDefault(assignment.PrincipalId) ?? [])
            {
                Add(result, userId, assignment.RoleDefinitionId);
            }
        }

        return result.ToDictionary(p => p.Key, p => (IReadOnlySet<string>)p.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static void Add(Dictionary<string, HashSet<string>> sets, string key, string value)
    {
        if (!sets.TryGetValue(key, out var set))
        {
            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            sets[key] = set;
        }

        set.Add(value);
    }

    private static (Severity Severity, string Message) Describe(
        DirectoryUser user,
        MfaRequirement requirement,
        bool isAdmin)
    {
        var severity = isAdmin ? Severity.Critical : user.UserType == UserType.Guest ? Severity.Info : Severity.Warning;
        string message;

        if (requirement.SometimesRequiredBy.Count > 0)
        {
            severity = OneLower(severity);
            message = $"MFA is only required in some cases, by: {Names(requirement.SometimesRequiredBy)}.";
        }
        else
        {
            message = NotRequiredMessage;
        }

        if (requirement.ReportOnlyRequiredBy.Count > 0)
        {
            message += $" Report-only policies that would require MFA: {Names(requirement.ReportOnlyRequiredBy)}.";
        }

        return (severity, message);
    }

    private static Severity OneLower(Severity severity) =>
        severity switch
        {
            Severity.Critical => Severity.Warning,
            _ => Severity.Info,
        };

    private static string Names(IEnumerable<ConditionalAccessPolicy> policies) =>
        string.Join(", ", policies.Select(p => p.DisplayName));

    // The date of the most recent one is in its own column, so it is shown in the UTC or local time chosen in Settings.
    private static string SignInNote(SingleFactorSignIns signIns, int days) =>
        $" {signIns.Count} successful single-factor sign-in{(signIns.Count == 1 ? string.Empty : "s")} in the last " +
        $"{(days == 1 ? "day" : $"{days} days")}.";

    private Finding SecurityDefaultsFinding() =>
        new()
        {
            CheckId = Id,
            Severity = Severity.Info,
            ObjectType = "Tenant",
            ObjectId = string.Empty,
            ObjectDisplayName = "Security defaults",
            Message = SecurityDefaultsMessage,
        };

    private Finding MakeFinding(DirectoryUser user, Severity severity, string message, DateTimeOffset? lastSignIn) =>
        new()
        {
            CheckId = Id,
            Severity = severity,
            ObjectType = "User",
            ObjectId = user.Id,
            ObjectDisplayName = user.DisplayName,
            UserPrincipalName = user.UserPrincipalName,
            Message = message,
            DueDate = lastSignIn,
        };
}
