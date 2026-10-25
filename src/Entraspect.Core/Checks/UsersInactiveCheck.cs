using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Entraspect.Core.Checks;

public sealed class UsersInactiveCheck : ICheck
{
    public const string DisabledNote = " The account is disabled; consider deleting it.";

    public const string SyncedNote = " This account is managed in on-premises Active Directory.";

    private readonly TimeProvider _timeProvider;

    public UsersInactiveCheck(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public string Id => "users-inactive";

    public string Title => "Inactive users";

    public string Description =>
        "Members, cloud-only or synced from on-premises, with no successful sign-in for at least the number of days " +
        "set in Settings > Checks. Microsoft started recording successful sign-ins in December 2023; for users without " +
        "one, the last sign-in attempt counts instead. Users who never signed in are reported once they were created " +
        "that long ago. An inactive user holding a privileged role is Critical. Disabled users are Info, unless they " +
        "hold a role: Disabled users with access reports those.";

    public CheckCategory Category => CheckCategory.Users;

    // User.Read.All: every member. AuditLog.Read.All: their sign-in activity. RoleManagement.Read.Directory: role
    // assignments and role names. GroupMember.Read.All: members of role-assignable groups.
    public IReadOnlyList<string> RequiredPermissions =>
        new[] { "User.Read.All", "AuditLog.Read.All", "RoleManagement.Read.Directory", "GroupMember.Read.All" };

    // Sign-in activity needs Entra ID P1 or P2.
    public LicenceRequirement RequiredLicence => LicenceRequirement.EntraIdP1;

    public FindingLabels Labels { get; } = UserCheckRules.Labels(dueDate: "Last sign-in", value: "Source");

    public async Task<IReadOnlyList<Finding>> RunAsync(
        IDirectoryReader reader,
        CheckOptions options,
        CancellationToken cancellationToken = default)
    {
        var users = await reader.GetUsersAsync(cancellationToken).ConfigureAwait(false);
        var activity = new Dictionary<string, UserSignInActivity>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in await reader.GetUserSignInActivityAsync(cancellationToken).ConfigureAwait(false))
        {
            activity[entry.UserId] = entry;
        }

        var now = _timeProvider.GetUtcNow();
        var inactive = new List<(DirectoryUser User, Severity Severity, string Message, DateTimeOffset? Last)>();
        foreach (var user in users.Where(u => !UserCheckRules.IsGuest(u)))
        {
            if (Evaluate(user, activity.GetValueOrDefault(user.Id), options, now) is (var severity, var message, var last))
            {
                inactive.Add((user, severity, message, last));
            }
        }

        if (inactive.Count == 0)
        {
            return [];
        }

        var assignments = await reader.GetDirectoryRoleAssignmentsAsync(cancellationToken).ConfigureAwait(false);
        var definitions = await reader.GetDirectoryRoleDefinitionsAsync(cancellationToken).ConfigureAwait(false);
        var held = await RoleHolders.GetAsync(users, assignments, definitions, reader, cancellationToken)
            .ConfigureAwait(false);
        var findings = new List<Finding>();

        foreach (var (user, baseSeverity, baseMessage, last) in inactive)
        {
            var roles = held.GetValueOrDefault(user.Id) ?? [];
            var severity = baseSeverity;
            var message = baseMessage;

            if (!user.AccountEnabled)
            {
                // Disabled users with access reports a disabled user who holds a role.
                if (roles.Count > 0)
                {
                    continue;
                }

                severity = Severity.Info;
                message += DisabledNote;
                if (user.OnPremisesSyncEnabled == true)
                {
                    message += SyncedNote;
                }
            }
            else if (HoldsText(roles) is { } holds)
            {
                severity = Severity.Critical;
                message += holds;
            }

            findings.Add(UserCheckRules.MakeFinding(
                Id,
                user,
                severity,
                message,
                dueDate: last,
                value: UserCheckRules.SourceText(user)));
        }

        return findings;
    }

    /// <summary>
    /// " Holds ROLE." naming the user's highest-tier privileged role, with how many other privileged roles they hold;
    /// null when they hold no Tier 0 or Privileged role.
    /// </summary>
    private static string? HoldsText(IReadOnlyList<HeldRole> roles)
    {
        // Roles come highest tier first, then by name.
        var privileged = roles.Where(r => r.Tier >= RoleTier.Privileged).ToList();
        return privileged.Count switch
        {
            0 => null,
            1 => $" Holds {privileged[0].Name}.",
            2 => $" Holds {privileged[0].Name} and 1 more role.",
            _ => $" Holds {privileged[0].Name} and {privileged.Count - 1} more roles.",
        };
    }

    /// <summary>The finding for an inactive user, before roles and the account state, or null when not reported.</summary>
    private static (Severity Severity, string Message, DateTimeOffset? LastActivity)? Evaluate(
        DirectoryUser user,
        UserSignInActivity? activity,
        CheckOptions options,
        DateTimeOffset now)
    {
        if (UserCheckRules.LastActivity(activity) is (var last, var successful))
        {
            var days = UserCheckRules.DaysSince(last, now);
            if (days < options.UserInactiveDays)
            {
                return null;
            }

            // Without a successful sign-in on record, the latest attempt counts, which may have failed.
            return successful
                ? (Severity.Warning, $"No successful sign-in for {UserCheckRules.FormatDays(days)}.", last)
                : (Severity.Warning, $"No sign-in attempt for {UserCheckRules.FormatDays(days)}.", last);
        }

        if (user.CreatedDateTime is not { } created)
        {
            return (Severity.Info, "Never signed in.", null);
        }

        var age = UserCheckRules.DaysSince(created, now);
        return age >= options.UserInactiveDays
            ? (Severity.Warning, $"Never signed in since being created {UserCheckRules.FormatDays(age)} ago.", null)
            : null;
    }
}
