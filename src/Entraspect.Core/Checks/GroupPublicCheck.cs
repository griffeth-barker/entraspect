using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Entraspect.Core.Checks;

public sealed class GroupPublicCheck : ICheck
{
    public string Id => "group-public";

    public string Title => "Public groups";

    public string Description =>
        "Surfaces public Microsoft 365 groups. Anyone in the organisation can join a public group and read its " +
        "content, such as its conversations and files. Make sure each one is meant to be open.";

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
            if (group.Kind == GroupKind.Microsoft365 && group.Visibility == GroupVisibility.Public)
            {
                findings.Add(new Finding
                {
                    CheckId = Id,
                    Severity = Severity.Info,
                    ObjectType = GroupCheckRules.ObjectType,
                    ObjectId = group.Id,
                    ObjectDisplayName = group.DisplayName,
                    Message = "Anyone in the organisation can join this Microsoft 365 group and read its content.",
                });
            }
        }

        return findings;
    }
}
