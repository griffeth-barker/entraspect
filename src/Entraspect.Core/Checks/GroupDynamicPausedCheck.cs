using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Entraspect.Core.Checks;

public sealed class GroupDynamicPausedCheck : ICheck
{
    public string Id => "group-dynamic-paused";

    public string Title => "Paused dynamic groups";

    public string Description =>
        "Surfaces dynamic groups whose membership processing is paused. While it is paused, members are no longer " +
        "added or removed as users change, so access and licences given through the group go stale. Processing is " +
        "sometimes paused on purpose, for example while a rule is being changed.";

    public CheckCategory Category => CheckCategory.Groups;

    public IReadOnlyList<string> RequiredPermissions => new[] { "GroupMember.Read.All" };

    public FindingLabels Labels => GroupCheckRules.Labels;

    public async Task<IReadOnlyList<Finding>> RunAsync(
        IDirectoryReader reader,
        CheckOptions options,
        CancellationToken cancellationToken = default)
    {
        var groups = await reader.GetGroupsAsync(cancellationToken).ConfigureAwait(false);
        var findings = new List<Finding>();

        foreach (var group in groups)
        {
            if (group.IsDynamic && group.IsDynamicProcessingPaused)
            {
                findings.Add(new Finding
                {
                    CheckId = Id,
                    Severity = Severity.Warning,
                    ObjectType = GroupCheckRules.ObjectType,
                    ObjectId = group.Id,
                    ObjectDisplayName = group.DisplayName,
                    Message = "Dynamic membership processing is paused, so members are no longer added or removed.",
                });
            }
        }

        return findings;
    }
}
