using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Entraspect.Core.Checks;

public sealed class GroupLicenseErrorsCheck : ICheck
{
    public string Id => "group-license-errors";

    public string Title => "Licence errors";

    public string Description =>
        "Surfaces groups that assign licences (group-based licensing) where some members didn't get them, for " +
        "example because there weren't enough licences or two service plans conflict. The group's Licenses page in " +
        "the Entra admin center lists the members and errors.";

    public CheckCategory Category => CheckCategory.Groups;

    public IReadOnlyList<string> RequiredPermissions => new[] { "GroupMember.Read.All" };

    // Group-based licensing needs Entra ID P1 or P2.
    public LicenceRequirement RequiredLicence => LicenceRequirement.EntraIdP1;

    public FindingLabels Labels => GroupCheckRules.Labels;

    public async Task<IReadOnlyList<Finding>> RunAsync(
        IDirectoryReader reader,
        CheckOptions options,
        CancellationToken cancellationToken = default)
    {
        // Read first, so that a failure here fails this check before the group listing is read.
        var ids = await reader.GetGroupIdsWithLicenseErrorsAsync(cancellationToken).ConfigureAwait(false);
        if (ids.Count == 0)
        {
            return [];
        }

        var withErrors = ids.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var groups = await reader.GetGroupsAsync(cancellationToken).ConfigureAwait(false);
        var findings = new List<Finding>();

        // A group created or deleted between the two reads is left out until the next scan.
        foreach (var group in groups)
        {
            if (withErrors.Contains(group.Id))
            {
                findings.Add(new Finding
                {
                    CheckId = Id,
                    Severity = Severity.Warning,
                    ObjectType = GroupCheckRules.ObjectType,
                    ObjectId = group.Id,
                    ObjectDisplayName = group.DisplayName,
                    Message =
                        $"Some members of this {GroupCheckRules.KindNoun(group.Kind)} didn't get the licences it assigns.",
                });
            }
        }

        return findings;
    }
}
