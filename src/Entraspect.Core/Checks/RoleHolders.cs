using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Entraspect.Core.Checks;

/// <summary>
/// A directory role a user holds. <see cref="Id"/> is the role assignment's role definition ID, the template ID for
/// built-in roles; <see cref="Name"/> falls back to it when the role has no name.
/// </summary>
public sealed record HeldRole(string Id, string Name, RoleTier Tier);

/// <summary>Works out which directory roles each user holds, for every check that needs it.</summary>
public static class RoleHolders
{
    /// <summary>
    /// The distinct roles each user holds through an active assignment, at any scope, directly or through a
    /// role-assignable group, keyed by user ID (case-insensitive). Each user's roles are ordered highest tier first,
    /// then by name. Users without roles aren't in the result. A principal that isn't one of
    /// <paramref name="users"/> is read as a group: role-assignable groups have members, other principals (service
    /// principals) have none.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, IReadOnlyList<HeldRole>>> GetAsync(
        IReadOnlyCollection<DirectoryUser> users,
        IReadOnlyList<DirectoryRoleAssignment> assignments,
        IReadOnlyList<DirectoryRoleDefinition> definitions,
        IDirectoryReader reader,
        CancellationToken cancellationToken = default)
    {
        var userIds = users.Select(u => u.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var roles = Roles(definitions);
        var held = new Dictionary<string, Dictionary<string, HeldRole>>(StringComparer.OrdinalIgnoreCase);

        foreach (var assignment in assignments)
        {
            IEnumerable<string> holders = userIds.Contains(assignment.PrincipalId)
                ? [assignment.PrincipalId]
                : ConditionalAccessPolicyHygieneCheck.IsObjectId(assignment.PrincipalId)
                    ? await reader.GetGroupTransitiveUserIdsAsync(assignment.PrincipalId, cancellationToken)
                        .ConfigureAwait(false)
                    : [];

            foreach (var holder in holders)
            {
                if (!held.TryGetValue(holder, out var userRoles))
                {
                    userRoles = new Dictionary<string, HeldRole>(StringComparer.OrdinalIgnoreCase);
                    held[holder] = userRoles;
                }

                userRoles.TryAdd(assignment.RoleDefinitionId, Role(assignment.RoleDefinitionId, roles));
            }
        }

        return Ordered(held);
    }

    /// <summary>
    /// The distinct roles assigned directly to each principal (user, group or service principal) through an active
    /// assignment, at any scope, keyed by principal ID (case-insensitive). Group members aren't read: a group's roles
    /// stay with the group. Each principal's roles are ordered highest tier first, then by name.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<HeldRole>> GetPrincipalRoles(
        IReadOnlyList<DirectoryRoleAssignment> assignments,
        IReadOnlyList<DirectoryRoleDefinition> definitions)
    {
        var roles = Roles(definitions);
        var held = new Dictionary<string, Dictionary<string, HeldRole>>(StringComparer.OrdinalIgnoreCase);

        foreach (var assignment in assignments)
        {
            if (!held.TryGetValue(assignment.PrincipalId, out var principalRoles))
            {
                principalRoles = new Dictionary<string, HeldRole>(StringComparer.OrdinalIgnoreCase);
                held[assignment.PrincipalId] = principalRoles;
            }

            principalRoles.TryAdd(assignment.RoleDefinitionId, Role(assignment.RoleDefinitionId, roles));
        }

        return Ordered(held);
    }

    /// <summary>
    /// The IDs of the principals assigned, directly and actively, at any scope, one of the built-in roles with the
    /// given template IDs. An assignment matches by template ID, or by the ID of a definition with that template ID.
    /// </summary>
    public static IReadOnlySet<string> GetDirectHolders(
        IReadOnlyList<DirectoryRoleAssignment> assignments,
        IReadOnlyList<DirectoryRoleDefinition> definitions,
        IReadOnlyCollection<string> templateIds)
    {
        var wanted = templateIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var roles = Roles(definitions);

        return assignments
            .Where(a => wanted.Contains(a.RoleDefinitionId) ||
                (roles.GetValueOrDefault(a.RoleDefinitionId)?.TemplateId is { Length: > 0 } templateId &&
                    wanted.Contains(templateId)))
            .Select(a => a.PrincipalId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<HeldRole>> Ordered(
        Dictionary<string, Dictionary<string, HeldRole>> held) =>
        held.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<HeldRole>)entry.Value.Values
                .OrderByDescending(r => r.Tier)
                .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            StringComparer.OrdinalIgnoreCase);

    private static HeldRole Role(string roleDefinitionId, Dictionary<string, DirectoryRoleDefinition> roles)
    {
        var definition = roles.GetValueOrDefault(roleDefinitionId);

        // A built-in role's definition ID may differ from its template ID, which the tiers are keyed by.
        var tier = PrivilegedRoles.TierOf(roleDefinitionId);
        if (tier == RoleTier.Other && definition?.TemplateId is { Length: > 0 } templateId)
        {
            tier = PrivilegedRoles.TierOf(templateId);
        }

        var name = definition?.DisplayName is { Length: > 0 } displayName ? displayName : roleDefinitionId;
        return new HeldRole(roleDefinitionId, name, tier);
    }

    /// <summary>
    /// Role definitions by ID and by template ID, since assignments name built-in roles by template ID. Definitions
    /// without a name are kept, so a built-in role still gets its tier, but a named one wins.
    /// </summary>
    private static Dictionary<string, DirectoryRoleDefinition> Roles(IEnumerable<DirectoryRoleDefinition> definitions)
    {
        var result = new Dictionary<string, DirectoryRoleDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in definitions)
        {
            Add(definition.Id, definition);
            if (definition.TemplateId is { Length: > 0 } templateId)
            {
                Add(templateId, definition);
            }
        }

        return result;

        void Add(string key, DirectoryRoleDefinition definition)
        {
            if (!result.TryGetValue(key, out var existing) ||
                (existing.DisplayName is not { Length: > 0 } && definition.DisplayName is { Length: > 0 }))
            {
                result[key] = definition;
            }
        }
    }
}
