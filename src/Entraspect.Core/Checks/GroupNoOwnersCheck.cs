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
        "Surfaces Microsoft 365 and security groups with no owners, including security groups synced from " +
        "on-premises, which are Info because their owner may be set only in Active Directory (managedBy). " +
        "Distribution and mail-enabled security groups are skipped: Microsoft Graph documents their owners as " +
        "unavailable. Groups converted to cloud management are checked like cloud groups. Groups newer than the " +
        "new group grace period are skipped.";

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
        if (group.HasOwners is not false || GroupCheckRules.IsNew(group, options, now))
        {
            return null;
        }

        // Active Directory's managedBy isn't synced to Entra ID, so a synced group can have an owner Graph can't
        // see. Only synced security groups are reported; for other synced kinds owners aren't available at all.
        if (group.IsSyncedFromOnPremises)
        {
            return group.Kind == GroupKind.Security
                ? MakeFinding(
                    group,
                    Severity.Info,
                    "Synced security group has no owners in Entra ID. Its owner may be set only in Active Directory " +
                    "(managedBy), which isn't synced.")
                : null;
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
