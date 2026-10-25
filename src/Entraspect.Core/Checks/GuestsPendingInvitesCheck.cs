using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Entraspect.Core.Checks;

public sealed class GuestsPendingInvitesCheck : ICheck
{
    public const string Advice = " Resend it, or delete the guest if it's no longer needed.";

    private readonly TimeProvider _timeProvider;

    public GuestsPendingInvitesCheck(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public string Id => "guests-pending-invites";

    public string Title => "Pending guest invitations";

    public string Description =>
        "Guest invitations not accepted after the number of days set in Settings > Checks. An invitation nobody " +
        "accepted is usually a stale or mistyped address. The invitation date is when the invitation state last " +
        "changed, or when the guest was created.";

    public CheckCategory Category => CheckCategory.Users;

    // User.Read.All: every guest and their invitation state.
    public IReadOnlyList<string> RequiredPermissions => new[] { "User.Read.All" };

    public FindingLabels Labels { get; } = GuestCheckRules.Labels(dueDate: "Invited");

    public async Task<IReadOnlyList<Finding>> RunAsync(
        IDirectoryReader reader,
        CheckOptions options,
        CancellationToken cancellationToken = default)
    {
        var pending = (await reader.GetUsersAsync(cancellationToken).ConfigureAwait(false))
            .Where(u => GuestCheckRules.IsGuest(u) && u.ExternalUserState == ExternalUserState.PendingAcceptance);
        var now = _timeProvider.GetUtcNow();
        var findings = new List<Finding>();

        foreach (var guest in pending)
        {
            var invited = guest.ExternalUserStateChangeDateTime ?? guest.CreatedDateTime;
            if (invited is null)
            {
                // With no date, how long it has been pending is unknown, so it is reported.
                findings.Add(GuestCheckRules.MakeFinding(Id, guest, Severity.Info, "Invitation not accepted." + Advice));
                continue;
            }

            var days = GuestCheckRules.DaysSince(invited.Value, now);
            if (days >= options.GuestPendingInviteDays)
            {
                findings.Add(GuestCheckRules.MakeFinding(
                    Id,
                    guest,
                    Severity.Info,
                    $"Invitation not accepted after {GuestCheckRules.FormatDays(days)}." + Advice,
                    dueDate: invited));
            }
        }

        return findings;
    }
}
