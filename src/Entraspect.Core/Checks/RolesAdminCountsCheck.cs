using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Entraspect.Core.Checks;

public sealed class RolesAdminCountsCheck : ICheck
{
    public const string RoleObjectType = "Role";

    public string Id => "roles-admin-counts";

    public string Title => "Admin role counts";

    public string Description =>
        "How many users hold Global Administrator and how many privileged role assignments there are. Microsoft " +
        "recommends fewer than five Global Administrators but at least two, such as two emergency access accounts, " +
        "and fewer than ten privileged role assignments. Assignments count when active, at any scope, directly or " +
        "through a role-assignable group, once per user who gets the role; eligible (PIM) assignments aren't included.";

    public CheckCategory Category => CheckCategory.RolesAndAdmins;

    public IReadOnlyList<string> RequiredPermissions => AdminCheckRules.RequiredPermissions;

    // Findings are about roles, not users.
    public FindingLabels Labels { get; } = new()
    {
        ObjectDisplayName = "Role",
        UserPrincipalName = null,
        ObjectId = "Object ID",
        SubObjectDisplayName = null,
        SubObjectType = null,
        DueDate = null,
        Value = "Holders",
    };

    public async Task<IReadOnlyList<Finding>> RunAsync(
        IDirectoryReader reader,
        CheckOptions options,
        CancellationToken cancellationToken = default)
    {
        var users = await reader.GetUsersAsync(cancellationToken).ConfigureAwait(false);
        var assignments = await reader.GetDirectoryRoleAssignmentsAsync(cancellationToken).ConfigureAwait(false);
        var definitions = await reader.GetDirectoryRoleDefinitionsAsync(cancellationToken).ConfigureAwait(false);
        var held = await RoleHolders.GetAsync(users, assignments, definitions, reader, cancellationToken)
            .ConfigureAwait(false);

        // A built-in role's definition ID may differ from its template ID.
        var globalAdminIds = definitions
            .Where(d => string.Equals(d.TemplateId, PrivilegedRoles.GlobalAdministrator, StringComparison.OrdinalIgnoreCase))
            .Select(d => d.Id)
            .Append(PrivilegedRoles.GlobalAdministrator)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var globalAdmins = held.Values.Count(roles => roles.Any(r => globalAdminIds.Contains(r.Id)));
        var privilegedAssignments = held.Values.Sum(roles => roles.Count(AdminCheckRules.IsPrivileged));
        var findings = new List<Finding>();

        if (globalAdmins >= 5)
        {
            findings.Add(GlobalAdminFinding(
                globalAdmins,
                $"{globalAdmins} users hold Global Administrator. Microsoft recommends fewer than five."));
        }
        else if (globalAdmins < 2)
        {
            var holders = globalAdmins == 1 ? "1 user holds" : $"{globalAdmins} users hold";
            findings.Add(GlobalAdminFinding(
                globalAdmins,
                $"Only {holders} Global Administrator. Keep at least two, such as two emergency access accounts, so " +
                "you can't lose access to the tenant."));
        }

        if (privilegedAssignments >= 10)
        {
            findings.Add(new Finding
            {
                CheckId = Id,
                Severity = Severity.Info,
                ObjectType = RoleObjectType,
                ObjectId = string.Empty,
                ObjectDisplayName = "Privileged roles",
                Message = $"{privilegedAssignments} privileged role assignments. Microsoft recommends fewer than ten.",
                Value = privilegedAssignments.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
        }

        return findings;
    }

    private Finding GlobalAdminFinding(int holders, string message) => new()
    {
        CheckId = Id,
        Severity = Severity.Warning,
        ObjectType = RoleObjectType,
        ObjectId = PrivilegedRoles.GlobalAdministrator,
        ObjectDisplayName = PrivilegedRoles.Tier0[PrivilegedRoles.GlobalAdministrator],
        Message = message,
        Value = holders.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}
