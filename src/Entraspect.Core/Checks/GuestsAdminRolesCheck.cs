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

    public FindingLabels Labels { get; } = UserCheckRules.Labels(dueDate: null, subObjectDisplayName: "Role");

    public async Task<IReadOnlyList<Finding>> RunAsync(
        IDirectoryReader reader,
        CheckOptions options,
        CancellationToken cancellationToken = default)
    {
        var users = await reader.GetUsersAsync(cancellationToken).ConfigureAwait(false);
        var assignments = await reader.GetDirectoryRoleAssignmentsAsync(cancellationToken).ConfigureAwait(false);
        var definitions = await reader.GetDirectoryRoleDefinitionsAsync(cancellationToken).ConfigureAwait(false);

        var guests = users.Where(UserCheckRules.IsGuest)
            .GroupBy(u => u.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        if (guests.Count == 0)
        {
            return [];
        }

        var held = await RoleHolders.GetAsync(users, assignments, definitions, reader, cancellationToken)
            .ConfigureAwait(false);
        var findings = new List<Finding>();

        foreach (var (userId, roles) in held)
        {
            if (!guests.TryGetValue(userId, out var guest))
            {
                continue;
            }

            foreach (var role in roles)
            {
                findings.Add(UserCheckRules.MakeFinding(
                    Id,
                    guest,
                    Severity.Critical,
                    $"Guest holds the {role.Name} role. Their identity is managed by another organisation.",
                    subObjectDisplayName: role.Name));
            }
        }

        return findings;
    }
}
