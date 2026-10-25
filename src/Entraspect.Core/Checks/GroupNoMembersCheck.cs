using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Entraspect.Core.Checks;

public sealed class GroupNoMembersCheck : ICheck
{
    private readonly TimeProvider _timeProvider;

    public GroupNoMembersCheck(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public string Id => "group-no-members";

    public string Title => "Groups with no members";

    public string Description =>
        "Surfaces groups with no direct members, including groups synced from on-premises. Empty dynamic groups are " +
        "Info: their membership rule matches no one. For synced groups, members outside the sync scope aren't in " +
        "Entra ID and aren't counted. Groups newer than the new group grace period are skipped.";

    public CheckCategory Category => CheckCategory.Groups;

    // Member.Read.Hidden: without it, groups with hidden membership look empty.
    public IReadOnlyList<string> RequiredPermissions => new[] { "GroupMember.Read.All", "Member.Read.Hidden" };

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
            if (group.HasMembers || GroupCheckRules.IsNew(group, options, now))
            {
                continue;
            }

            if (group.IsDynamic)
            {
                var kind = GroupCheckRules.KindName(group.Kind, "Dynamic");
                findings.Add(MakeFinding(group, Severity.Info, $"{kind} has no members; its rule matches no one."));
            }
            else if (group.IsSyncedFromOnPremises)
            {
                var kind = GroupCheckRules.KindName(group.Kind, "Synced");
                findings.Add(MakeFinding(
                    group,
                    Severity.Warning,
                    $"{kind} has no members in Entra ID. Members outside the sync scope aren't synced, so cloud " +
                    "policies and assignments that use it reach no one."));
            }
            else
            {
                findings.Add(MakeFinding(group, Severity.Warning, $"{GroupCheckRules.KindName(group.Kind)} has no members."));
            }
        }

        return findings;
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
