using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Entraspect.Core.Checks;

public sealed class RolesLicensedAdminsCheck : ICheck
{
    public string Id => "roles-licensed-admins";

    public string Title => "Licensed admins";

    public string Description =>
        "Admins whose account has licences assigned, directly or through groups. A separate, unlicensed admin account " +
        "keeps email and everyday apps, the usual ways in, away from admin rights. Admins are users holding a Tier 0 " +
        "or other privileged role, active, at any scope, directly or through a role-assignable group. Always Info.";

    public CheckCategory Category => CheckCategory.RolesAndAdmins;

    // User.Read.All also gives each user's assigned licences.
    public IReadOnlyList<string> RequiredPermissions => AdminCheckRules.RequiredPermissions;

    public FindingLabels Labels { get; } = AdminCheckRules.Labels(value: "Licences");

    public async Task<IReadOnlyList<Finding>> RunAsync(
        IDirectoryReader reader,
        CheckOptions options,
        CancellationToken cancellationToken = default)
    {
        var admins = await AdminCheckRules.GetAdminsAsync(reader, cancellationToken).ConfigureAwait(false);

        return admins
            .Where(a => a.User.AssignedLicenceCount > 0)
            .Select(a =>
            {
                var count = a.User.AssignedLicenceCount;
                var licences = count == 1 ? "1 licence" : $"{count} licences";
                return AdminCheckRules.MakeFinding(
                    Id,
                    a,
                    Severity.Info,
                    $"Admin account has {licences} assigned. Use a separate, unlicensed account for admin work, so " +
                    "email and everyday apps aren't used with admin rights.",
                    value: count.ToString(CultureInfo.InvariantCulture));
            })
            .ToList();
    }
}
