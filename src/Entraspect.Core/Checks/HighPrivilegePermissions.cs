using System.Collections.Generic;

namespace Entraspect.Core.Checks;

/// <summary>
/// Microsoft Graph application permissions the high-privilege apps check reports, in two tiers. entraspect only
/// reads these names on other apps; it never requests any of them.
/// </summary>
public static class HighPrivilegePermissions
{
    /// <summary>
    /// Critical: a permission an app can use to take over the tenant, for example by granting itself or others more
    /// permissions or admin roles, adding credentials to other apps, or changing sign-in policy and methods.
    /// </summary>
    public static IReadOnlySet<string> Critical { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "RoleManagement.ReadWrite.Directory",
        "AppRoleAssignment.ReadWrite.All",
        "Application.ReadWrite.All",
        "Directory.ReadWrite.All",
        "RoleAssignmentSchedule.ReadWrite.Directory",
        "RoleEligibilitySchedule.ReadWrite.Directory",
        "PrivilegedAccess.ReadWrite.AzureADGroup",
        "UserAuthenticationMethod.ReadWrite.All",
        "Policy.ReadWrite.ConditionalAccess",
    };

    /// <summary>
    /// Warning: a permission that reaches data across the whole tenant, such as every mailbox, file or site, or
    /// changes every user or group, without directly granting more privilege.
    /// </summary>
    public static IReadOnlySet<string> Warning { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Mail.ReadWrite",
        "Mail.Send",
        "Files.ReadWrite.All",
        "Sites.ReadWrite.All",
        "Sites.FullControl.All",
        "User.ReadWrite.All",
        "Group.ReadWrite.All",
        "GroupMember.ReadWrite.All",
    };

    /// <summary>The permission's tier, or null when it is in neither list.</summary>
    public static Severity? SeverityOf(string permissionName) =>
        Critical.Contains(permissionName) ? Severity.Critical
        : Warning.Contains(permissionName) ? Severity.Warning
        : null;
}
