using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Entraspect.Core.Checks;

public sealed class GroupSingleOwnerCheck : ICheck
{
    private readonly TimeProvider _timeProvider;

    public GroupSingleOwnerCheck(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public string Id => "group-single-owner";

    public string Title => "Groups with one owner";

    public string Description =>
        "Surfaces Microsoft 365 and security groups with exactly one owner, including security groups synced from " +
        "on-premises. If that owner leaves, no one can manage the group. Groups with no owners are under Groups with " +
        "no owners. Groups newer than the new group grace period are skipped.";

    public CheckCategory Category => CheckCategory.Groups;

    public IReadOnlyList<string> RequiredPermissions => new[] { "GroupMember.Read.All" };

    public FindingLabels Labels => GroupCheckRules.Labels;

    public async Task<IReadOnlyList<Finding>> RunAsync(
        IDirectoryReader reader,
        CheckOptions options,
        CancellationToken cancellationToken = default)
    {
        var groups = await reader.GetGroupsAsync(cancellationToken).ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow();
        var findings = new List<Finding>();

        foreach (var group in groups)
        {
            // OwnerCount is at most 20, the expand limit, so 20 means 20 or more and is never one owner.
            if (group.OwnerCount != 1 || GroupCheckRules.IsNew(group, options, now))
            {
                continue;
            }

            var message = group.Kind switch
            {
                GroupKind.Microsoft365 =>
                    "Microsoft 365 group has only one owner. If they leave, no one can manage its membership.",
                GroupKind.Security => "Security group has only one owner.",
                _ => null,
            };

            if (message is not null)
            {
                findings.Add(new Finding
                {
                    CheckId = Id,
                    Severity = Severity.Info,
                    ObjectType = GroupCheckRules.ObjectType,
                    ObjectId = group.Id,
                    ObjectDisplayName = group.DisplayName,
                    Message = message,
                });
            }
        }

        return findings;
    }
}
