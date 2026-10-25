using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Entraspect.Core.Checks;

public sealed class GuestsAdminRolesCheck : ICheck
{
    public string Id => "guests-admin-roles";

    public string Title => "Guests with admin roles";

    public string Description =>
        "Guests holding an active directory role, built-in or custom, at any scope, directly or through a " +
        "role-assignable group. A guest's identity is managed by another organisation. One finding per guest and role. " +
        "Eligible (PIM) assignments aren't included.";

    public CheckCategory Category => CheckCategory.Users;

    // User.Read.All: every guest. RoleManagement.Read.Directory: role assignments and role names.
    // GroupMember.Read.All: members of role-assignable groups.
    public IReadOnlyList<string> RequiredPermissions =>
        new[] { "User.Read.All", "RoleManagement.Read.Directory", "GroupMember.Read.All" };

    public FindingLabels Labels { get; } = GuestCheckRules.Labels(dueDate: null, subObjectDisplayName: "Role");

    public async Task<IReadOnlyList<Finding>> RunAsync(
        IDirectoryReader reader,
        CheckOptions options,
        CancellationToken cancellationToken = default)
    {
        var users = await reader.GetUsersAsync(cancellationToken).ConfigureAwait(false);
        var assignments = await reader.GetDirectoryRoleAssignmentsAsync(cancellationToken).ConfigureAwait(false);
        var definitions = await reader.GetDirectoryRoleDefinitionsAsync(cancellationToken).ConfigureAwait(false);

        var guests = users.Where(GuestCheckRules.IsGuest)
            .GroupBy(u => u.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        if (guests.Count == 0)
        {
            return [];
        }

        var userIds = users.Select(u => u.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var roleNames = RoleNames(definitions);
        var seen = new HashSet<(string UserId, string RoleId)>();
        var findings = new List<Finding>();

        foreach (var assignment in assignments)
        {
            // Principals that aren't users are read as groups: role-assignable groups have members, other principals
            // (service principals) have none.
            IEnumerable<string> holders = userIds.Contains(assignment.PrincipalId)
                ? [assignment.PrincipalId]
                : ConditionalAccessPolicyHygieneCheck.IsObjectId(assignment.PrincipalId)
                    ? await reader.GetGroupTransitiveUserIdsAsync(assignment.PrincipalId, cancellationToken)
                        .ConfigureAwait(false)
                    : [];

            foreach (var holder in holders)
            {
                if (!guests.TryGetValue(holder, out var guest) ||
                    !seen.Add((guest.Id.ToUpperInvariant(), assignment.RoleDefinitionId.ToUpperInvariant())))
                {
                    continue;
                }

                var role = roleNames.GetValueOrDefault(assignment.RoleDefinitionId) ?? assignment.RoleDefinitionId;
                findings.Add(GuestCheckRules.MakeFinding(
                    Id,
                    guest,
                    Severity.Critical,
                    $"Guest holds the {role} role. Their identity is managed by another organisation.",
                    subObjectDisplayName: role));
            }
        }

        return findings;
    }

    /// <summary>
    /// Role names by role definition ID and by template ID, since assignments name built-in roles by template ID.
    /// </summary>
    private static Dictionary<string, string> RoleNames(IEnumerable<DirectoryRoleDefinition> definitions)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in definitions)
        {
            if (definition.DisplayName is not { Length: > 0 } name)
            {
                continue;
            }

            result.TryAdd(definition.Id, name);
            if (definition.TemplateId is { Length: > 0 } templateId)
            {
                result.TryAdd(templateId, name);
            }
        }

        return result;
    }
}
