using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Entraspect.Core.Checks;

public sealed class RolesGroupOwnersCheck : ICheck
{
    public string Id => "roles-group-owners";

    public string Title => "Role group owners";

    public string Description =>
        "Owners of role-assignable groups that hold a Tier 0 (Critical) or other privileged (Warning) role. A " +
        "role-assignable group is a group that can be assigned Entra roles, so every member holds them. A group's " +
        "owners can add members, including themselves, so each owner can give anyone the group's roles without " +
        "holding an admin role. Owners with a direct Global Administrator or Privileged Role Administrator " +
        "assignment aren't listed, since they can manage every role-assignable group anyway. Only active role " +
        "assignments to the group are included; eligible (PIM) assignments and PIM for Groups aren't.";

    public CheckCategory Category => CheckCategory.RolesAndAdmins;

    public IReadOnlyList<string> RequiredPermissions => AdminCheckRules.RequiredPermissions;

    public FindingLabels Labels { get; } = new()
    {
        ObjectDisplayName = "Group",
        ObjectId = "Object ID",
        SubObjectDisplayName = "Owner",
        SubObjectType = "Owner type",
        UserPrincipalName = "Owner UPN",
        DueDate = null,
        Value = "Roles",
    };

    // These roles can manage the membership of every role-assignable group, so owning one adds nothing.
    private static readonly string[] GroupManagerRoles =
        [PrivilegedRoles.GlobalAdministrator, PrivilegedRoles.PrivilegedRoleAdministrator];

    public async Task<IReadOnlyList<Finding>> RunAsync(
        IDirectoryReader reader,
        CheckOptions options,
        CancellationToken cancellationToken = default)
    {
        var users = await reader.GetUsersAsync(cancellationToken).ConfigureAwait(false);
        var assignments = await reader.GetDirectoryRoleAssignmentsAsync(cancellationToken).ConfigureAwait(false);
        var definitions = await reader.GetDirectoryRoleDefinitionsAsync(cancellationToken).ConfigureAwait(false);
        var groups = await reader.GetGroupsAsync(cancellationToken).ConfigureAwait(false);

        var principalRoles = RoleHolders.GetPrincipalRoles(assignments, definitions);
        var groupManagers = RoleHolders.GetDirectHolders(assignments, definitions, GroupManagerRoles);
        var usersById = new Dictionary<string, DirectoryUser>(StringComparer.OrdinalIgnoreCase);
        foreach (var user in users)
        {
            usersById.TryAdd(user.Id, user);
        }

        var seenGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var findings = new List<Finding>();
        foreach (var group in groups)
        {
            if (!group.IsRoleAssignable ||
                !seenGroups.Add(group.Id) ||
                principalRoles.GetValueOrDefault(group.Id) is not { } roles)
            {
                continue;
            }

            var privileged = roles.Where(AdminCheckRules.IsPrivileged).ToList();
            if (privileged.Count == 0)
            {
                continue;
            }

            var severity = privileged[0].Tier == RoleTier.Tier0 ? Severity.Critical : Severity.Warning;
            var rolesText = string.Join(", ", privileged.Select(r => r.Name));
            var owners = await reader.GetGroupOwnersAsync(group.Id, cancellationToken).ConfigureAwait(false);
            var seenOwners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var owner in owners)
            {
                var user = usersById.GetValueOrDefault(owner.Id);
                var isUser = owner.Kind == GroupOwnerKind.User || user is not null;
                if (!seenOwners.Add(owner.Id) || (isUser && groupManagers.Contains(owner.Id)))
                {
                    continue;
                }

                findings.Add(new Finding
                {
                    CheckId = Id,
                    Severity = severity,
                    ObjectType = GroupCheckRules.ObjectType,
                    ObjectId = group.Id,
                    ObjectDisplayName = group.DisplayName,
                    Message =
                        "Owner can add anyone to this group, including themselves, and so give them its roles. " +
                        "Remove the owner, or protect the account like an admin with these roles.",
                    SubObjectType = isUser ? "User" : OwnerKindText(owner.Kind),
                    SubObjectDisplayName = user?.DisplayName is { Length: > 0 } userName ? userName
                        : owner.DisplayName is { Length: > 0 } ownerName ? ownerName
                        : owner.Id,
                    UserPrincipalName = isUser
                        ? user?.UserPrincipalName is { Length: > 0 } upn ? upn : owner.UserPrincipalName
                        : null,
                    Value = rolesText,
                });
            }
        }

        return findings;
    }

    private static string OwnerKindText(GroupOwnerKind kind) =>
        kind switch
        {
            GroupOwnerKind.User => "User",
            GroupOwnerKind.ServicePrincipal => "Service principal",
            _ => "Other",
        };
}
