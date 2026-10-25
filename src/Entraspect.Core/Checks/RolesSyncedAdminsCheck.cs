using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Entraspect.Core.Checks;

public sealed class RolesSyncedAdminsCheck : ICheck
{
    public string Id => "roles-synced-admins";

    public string Title => "Synced admins";

    public string Description =>
        "Admins whose account is synced from on-premises Active Directory. Microsoft recommends cloud-only accounts " +
        "for admin work, so a compromise on-premises can't reach the cloud. Admins are users holding a Tier 0 " +
        "(Critical) or other privileged (Warning) role, active, at any scope, directly or through a role-assignable " +
        "group; eligible (PIM) assignments aren't included.";

    public CheckCategory Category => CheckCategory.RolesAndAdmins;

    public IReadOnlyList<string> RequiredPermissions => AdminCheckRules.RequiredPermissions;

    public FindingLabels Labels { get; } = AdminCheckRules.Labels();

    public async Task<IReadOnlyList<Finding>> RunAsync(
        IDirectoryReader reader,
        CheckOptions options,
        CancellationToken cancellationToken = default)
    {
        var admins = await AdminCheckRules.GetAdminsAsync(reader, cancellationToken).ConfigureAwait(false);

        return admins
            .Where(a => a.User.OnPremisesSyncEnabled == true)
            .Select(a => AdminCheckRules.MakeFinding(
                Id,
                a,
                a.Tier == RoleTier.Tier0 ? Severity.Critical : Severity.Warning,
                "Admin account is synced from on-premises Active Directory. Anyone who takes over that account or the " +
                "sync server gets these roles. Use a cloud-only account for admin work."))
            .ToList();
    }
}
