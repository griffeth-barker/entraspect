using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Entraspect.Core.Checks;

/// <summary>A user holding at least one Tier 0 or Privileged role, with those roles, highest tier first, then by name.</summary>
public sealed record Admin(DirectoryUser User, IReadOnlyList<HeldRole> Roles)
{
    /// <summary>The highest tier the admin holds.</summary>
    public RoleTier Tier => Roles[0].Tier;

    /// <summary>The admin's privileged roles as the Roles column shows them.</summary>
    public string RolesText => string.Join(", ", Roles.Select(r => r.Name));
}

/// <summary>Rules shared by the checks in Roles &amp; admins.</summary>
public static class AdminCheckRules
{
    // User.Read.All: every user. RoleManagement.Read.Directory: role assignments and role names.
    // GroupMember.Read.All: members of role-assignable groups.
    public static IReadOnlyList<string> RequiredPermissions { get; } =
        ["User.Read.All", "RoleManagement.Read.Directory", "GroupMember.Read.All"];

    public static bool IsPrivileged(HeldRole role) => role.Tier >= RoleTier.Privileged;

    /// <summary>The user columns, with the admin's privileged roles in a Roles column.</summary>
    public static FindingLabels Labels(string? value = null) =>
        UserCheckRules.Labels(dueDate: null, subObjectDisplayName: "Roles", value: value);

    /// <summary>
    /// Every user, member or guest, enabled or disabled, holding a Tier 0 or Privileged role through an active
    /// assignment, at any scope, directly or through a role-assignable group. In the order of the user list.
    /// </summary>
    public static async Task<IReadOnlyList<Admin>> GetAdminsAsync(
        IDirectoryReader reader,
        CancellationToken cancellationToken)
    {
        var users = await reader.GetUsersAsync(cancellationToken).ConfigureAwait(false);
        var assignments = await reader.GetDirectoryRoleAssignmentsAsync(cancellationToken).ConfigureAwait(false);
        var definitions = await reader.GetDirectoryRoleDefinitionsAsync(cancellationToken).ConfigureAwait(false);
        var held = await RoleHolders.GetAsync(users, assignments, definitions, reader, cancellationToken)
            .ConfigureAwait(false);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var admins = new List<Admin>();
        foreach (var user in users)
        {
            if (!seen.Add(user.Id) || held.GetValueOrDefault(user.Id) is not { } roles)
            {
                continue;
            }

            var privileged = roles.Where(IsPrivileged).ToList();
            if (privileged.Count > 0)
            {
                admins.Add(new Admin(user, privileged));
            }
        }

        return admins;
    }

    public static Finding MakeFinding(
        string checkId,
        Admin admin,
        Severity severity,
        string message,
        string? value = null) =>
        UserCheckRules.MakeFinding(
            checkId,
            admin.User,
            severity,
            message,
            subObjectDisplayName: admin.RolesText,
            value: value);
}
