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
        "Surfaces groups with no direct members. Empty dynamic groups are Info: their membership rule matches no one. " +
        "Groups newer than the new group grace period are skipped.";

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
            if (group.HasMembers || GroupCheckRules.IsNew(group, options, now))
            {
                continue;
            }

            var kind = GroupCheckRules.KindName(group.Kind, group.IsDynamic);
            findings.Add(group.IsDynamic
                ? MakeFinding(group, Severity.Info, $"{kind} has no members; its rule matches no one.")
                : MakeFinding(group, Severity.Warning, $"{kind} has no members."));
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
