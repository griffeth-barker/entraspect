using System.Collections.Generic;
using System.Linq;

namespace Entraspect.Core;

/// <summary>
/// Compares the granted permissions with every check's required permissions. Checks the tenant isn't licensed for
/// don't report missing permissions, but their permissions still count as used.
/// </summary>
public static class PermissionReview
{
    public sealed record MissingPermission(string Permission, IReadOnlyList<ICheck> Checks);

    public sealed record Result(
        IReadOnlyList<MissingPermission> MissingPermissions,
        IReadOnlyList<string> UnusedReadPermissions,
        IReadOnlyList<string> UnusedWritePermissions);

    public static Result Review(
        IReadOnlyList<ICheck> checks,
        IReadOnlyCollection<string> grantedPermissions,
        TenantLicence tenantLicence = TenantLicence.Unknown)
    {
        var granted = new HashSet<string>(grantedPermissions, StringComparer.Ordinal);
        var missingByPermission = new Dictionary<string, List<ICheck>>(StringComparer.Ordinal);

        var usedPermissions = new HashSet<string>(CheckCatalog.AppPermissions, StringComparer.Ordinal);
        foreach (var check in checks)
        {
            var licensed = Licensing.IsLicensed(check, tenantLicence);
            foreach (var permission in check.RequiredPermissions)
            {
                usedPermissions.Add(permission);
                if (granted.Contains(permission) || !licensed)
                {
                    continue;
                }

                if (!missingByPermission.TryGetValue(permission, out var list))
                {
                    list = new List<ICheck>();
                    missingByPermission[permission] = list;
                }

                list.Add(check);
            }
        }

        var missing = missingByPermission
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => new MissingPermission(p.Key, p.Value))
            .ToList();

        var unused = granted
            .Where(p => !usedPermissions.Contains(p))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        var unusedRead = new List<string>();
        var unusedWrite = new List<string>();
        foreach (var permission in unused)
        {
            if (IsWritePermission(permission))
            {
                unusedWrite.Add(permission);
            }
            else
            {
                unusedRead.Add(permission);
            }
        }

        return new Result(missing, unusedRead, unusedWrite);
    }

    /// <summary>
    /// A permission is treated as a write permission if its name contains "ReadWrite", or if it is otherwise not a
    /// read-only permission (for example "Mail.Send"). Read-only permissions contain ".Read".
    /// </summary>
    private static bool IsWritePermission(string permission) =>
        permission.Contains("ReadWrite", System.StringComparison.OrdinalIgnoreCase)
        || !permission.Contains(".Read", System.StringComparison.OrdinalIgnoreCase);
}
