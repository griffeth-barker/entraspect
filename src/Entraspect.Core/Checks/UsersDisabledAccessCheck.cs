using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Entraspect.Core.Checks;

public sealed class UsersDisabledAccessCheck : ICheck
{
    public string Id => "users-disabled-access";

    public string Title => "Disabled users with access";

    public string Description =>
        "Disabled members who still hold a directory role or have licences assigned. Re-enabling the account restores " +
        "that access at once, and licences cost money. Roles count when active, at any scope, directly or through a " +
        "role-assignable group; eligible (PIM) assignments aren't included. One finding per role, and one for licences.";

    public CheckCategory Category => CheckCategory.Users;

    // User.Read.All: every member, with their licences. RoleManagement.Read.Directory: role assignments and role names.
    // GroupMember.Read.All: members of role-assignable groups.
    public IReadOnlyList<string> RequiredPermissions =>
        new[] { "User.Read.All", "RoleManagement.Read.Directory", "GroupMember.Read.All" };

    public FindingLabels Labels { get; } =
        UserCheckRules.Labels(dueDate: null, subObjectDisplayName: "Access", value: "Source");

    public async Task<IReadOnlyList<Finding>> RunAsync(
        IDirectoryReader reader,
        CheckOptions options,
        CancellationToken cancellationToken = default)
    {
        var users = await reader.GetUsersAsync(cancellationToken).ConfigureAwait(false);
        var assignments = await reader.GetDirectoryRoleAssignmentsAsync(cancellationToken).ConfigureAwait(false);
        var definitions = await reader.GetDirectoryRoleDefinitionsAsync(cancellationToken).ConfigureAwait(false);

        var disabled = users.Where(u => !u.AccountEnabled && !UserCheckRules.IsGuest(u)).ToList();
        if (disabled.Count == 0)
        {
            return [];
        }

        var held = await RoleHolders.GetAsync(users, assignments, definitions, reader, cancellationToken)
            .ConfigureAwait(false);
        var findings = new List<Finding>();

        foreach (var user in disabled)
        {
            var synced = user.OnPremisesSyncEnabled == true ? UsersInactiveCheck.SyncedNote : string.Empty;
            var source = UserCheckRules.SourceText(user);

            foreach (var role in held.GetValueOrDefault(user.Id) ?? [])
            {
                findings.Add(UserCheckRules.MakeFinding(
                    Id,
                    user,
                    role.Tier >= RoleTier.Privileged ? Severity.Critical : Severity.Warning,
                    $"Disabled account still holds {role.Name}. Re-enabling it restores admin access; remove the role." +
                    synced,
                    subObjectDisplayName: role.Name,
                    value: source));
            }

            if (user.AssignedLicenceCount > 0)
            {
                var licences = user.AssignedLicenceCount == 1 ? "1 licence" : $"{user.AssignedLicenceCount} licences";
                findings.Add(UserCheckRules.MakeFinding(
                    Id,
                    user,
                    Severity.Info,
                    $"Disabled account still has {licences} assigned. Remove them if the user has left." + synced,
                    subObjectDisplayName: "Licences",
                    value: source));
            }
        }

        return findings;
    }
}
