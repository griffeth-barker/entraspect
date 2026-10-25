using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Entraspect.Core.Checks;

public sealed class GroupSyncErrorsCheck : ICheck
{
    public string Id => "group-sync-errors";

    public string Title => "Sync errors";

    public string Description =>
        "Surfaces errors syncing groups from on-premises Active Directory, one finding per error, for example a " +
        "property value that conflicts with another object. Until it is fixed, the group's changes in Active " +
        "Directory may not reach Entra ID.";

    public CheckCategory Category => CheckCategory.Groups;

    public IReadOnlyList<string> RequiredPermissions => new[] { "GroupMember.Read.All" };

    public FindingLabels Labels => GroupCheckRules.Labels with
    {
        SubObjectDisplayName = "Property",
        SubObjectType = "Category",
        DueDate = "Occurred",
    };

    public async Task<IReadOnlyList<Finding>> RunAsync(
        IDirectoryReader reader,
        CheckOptions options,
        CancellationToken cancellationToken = default)
    {
        var groups = await reader.GetGroupsAsync(cancellationToken).ConfigureAwait(false);
        var findings = new List<Finding>();

        foreach (var group in groups)
        {
            foreach (var error in group.SyncErrors)
            {
                findings.Add(new Finding
                {
                    CheckId = Id,
                    Severity = Severity.Warning,
                    ObjectType = GroupCheckRules.ObjectType,
                    ObjectId = group.Id,
                    ObjectDisplayName = group.DisplayName,
                    Message = Message(error),
                    SubObjectType = NullIfEmpty(error.Category),
                    SubObjectDisplayName = NullIfEmpty(error.PropertyCausingError),
                    DueDate = error.OccurredDateTime,
                });
            }
        }

        return findings;
    }

    /// <summary>"On-premises sync error on PROPERTY: VALUE.", leaving out the parts that are empty.</summary>
    public static string Message(GroupSyncError error)
    {
        var message = "On-premises sync error";
        if (!string.IsNullOrWhiteSpace(error.PropertyCausingError))
        {
            message += $" on {error.PropertyCausingError}";
        }

        if (!string.IsNullOrWhiteSpace(error.Value))
        {
            message += $": {error.Value}";
        }

        return message + ".";
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
