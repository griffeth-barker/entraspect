using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Entraspect.Core.Checks;

public sealed class GroupNoOwnersCheck : ICheck
{
    private readonly TimeProvider _timeProvider;

    public GroupNoOwnersCheck(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public string Id => "group-no-owners";

    public string Title => "Groups with no owners";

    public string Description =>
        "Surfaces Microsoft 365 and security groups with no owners. Synced, distribution and mail-enabled security " +
        "groups are skipped because Microsoft Graph doesn't return their owners. Groups newer than the new group " +
        "grace period are skipped.";

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
            var finding = Evaluate(group, options, now);
            if (finding is not null)
            {
                findings.Add(finding);
            }
        }

        return findings;
    }

    private Finding? Evaluate(DirectoryGroup group, CheckOptions options, DateTimeOffset now)
    {
        // Graph doesn't return owners for synced groups; the reader reports them as null, and this guards it.
        if (group.HasOwners is not false || group.IsSyncedFromOnPremises || GroupCheckRules.IsNew(group, options, now))
        {
            return null;
        }

        return group.Kind switch
        {
            GroupKind.Microsoft365 => MakeFinding(
                group,
                Severity.Warning,
                "Microsoft 365 group has no owners. No one can manage its membership, and it can't be renewed when " +
                "group expiration is on."),
            GroupKind.Security => MakeFinding(group, Severity.Info, "Security group has no owners."),
            _ => null,
        };
    }

    private Finding MakeFinding(DirectoryGroup group, Severity severity, string message) =>
        new()
        {
            CheckId = Id,
            Severity = severity,
            ObjectType = GroupCheckRules.ObjectType,
            ObjectId = group.Id,
            ObjectDisplayName = group.DisplayName,
            Message = message,
        };
}
