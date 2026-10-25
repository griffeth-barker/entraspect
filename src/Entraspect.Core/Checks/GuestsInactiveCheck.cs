using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Entraspect.Core.Checks;

public sealed class GuestsInactiveCheck : ICheck
{
    public const string DisabledNote = " The account is disabled; consider deleting it.";

    private readonly TimeProvider _timeProvider;

    public GuestsInactiveCheck(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public string Id => "guests-inactive";

    public string Title => "Inactive guests";

    public string Description =>
        "Guests, invited or synced from on-premises, with no successful sign-in for at least the number of days set in " +
        "Settings > Checks. Microsoft started recording successful sign-ins in December 2023; for guests without one, " +
        "the last sign-in attempt counts instead. Guests who never signed in are reported once they were added that " +
        "long ago. Disabled guests are Info.";

    public CheckCategory Category => CheckCategory.Users;

    // User.Read.All: every guest. AuditLog.Read.All: their sign-in activity.
    public IReadOnlyList<string> RequiredPermissions => new[] { "User.Read.All", "AuditLog.Read.All" };

    // Sign-in activity needs Entra ID P1 or P2.
    public LicenceRequirement RequiredLicence => LicenceRequirement.EntraIdP1;

    public FindingLabels Labels { get; } = UserCheckRules.Labels(dueDate: "Last sign-in", value: "Invitation");

    public async Task<IReadOnlyList<Finding>> RunAsync(
        IDirectoryReader reader,
        CheckOptions options,
        CancellationToken cancellationToken = default)
    {
        var guests = (await reader.GetUsersAsync(cancellationToken).ConfigureAwait(false))
            .Where(UserCheckRules.IsGuest)
            .ToList();
        var activity = new Dictionary<string, UserSignInActivity>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in await reader.GetUserSignInActivityAsync(cancellationToken).ConfigureAwait(false))
        {
            activity[entry.UserId] = entry;
        }

        var now = _timeProvider.GetUtcNow();
        var findings = new List<Finding>();

        foreach (var guest in guests)
        {
            if (Evaluate(guest, activity.GetValueOrDefault(guest.Id), options, now) is not { } result)
            {
                continue;
            }

            var (severity, message, last) = result;

            if (!guest.AccountEnabled)
            {
                severity = Severity.Info;
                message += DisabledNote;
            }

            findings.Add(UserCheckRules.MakeFinding(
                Id,
                guest,
                severity,
                message,
                dueDate: last,
                value: UserCheckRules.InvitationText(guest.ExternalUserState)));
        }

        return findings;
    }

    /// <summary>The finding for an enabled guest, or null when the guest isn't reported.</summary>
    private static (Severity Severity, string Message, DateTimeOffset? LastActivity)? Evaluate(
        DirectoryUser guest,
        UserSignInActivity? activity,
        CheckOptions options,
        DateTimeOffset now)
    {
        if (UserCheckRules.LastActivity(activity) is (var last, var successful))
        {
            var days = UserCheckRules.DaysSince(last, now);
            if (days < options.GuestInactiveDays)
            {
                return null;
            }

            // Without a successful sign-in on record, the latest attempt counts, which may have failed.
            return successful
                ? (Severity.Warning, $"No successful sign-in for {UserCheckRules.FormatDays(days)}.", last)
                : (Severity.Warning, $"No sign-in attempt for {UserCheckRules.FormatDays(days)}.", last);
        }

        if (guest.CreatedDateTime is not { } created)
        {
            return (Severity.Info, "Never signed in.", null);
        }

        var age = UserCheckRules.DaysSince(created, now);
        return age >= options.GuestInactiveDays
            ? (Severity.Warning, $"Never signed in since being added {UserCheckRules.FormatDays(age)} ago.", null)
            : null;
    }
}
