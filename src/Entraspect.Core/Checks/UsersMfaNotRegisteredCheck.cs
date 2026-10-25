using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Entraspect.Core.Checks;

public sealed class UsersMfaNotRegisteredCheck : ICheck
{
    private const string GuestNote =
        " Guests may do MFA in their home tenant, depending on your cross-tenant access settings.";

    public string Id => "users-mfa-not-registered";

    public string Title => "MFA not registered";

    public string Description =>
        "Answers \"who isn't registered for MFA\": users, including users synced from on-premises, who haven't " +
        "registered a multifactor authentication method that the current policy allows, so they can't do MFA. " +
        "Whether policy requires MFA is a separate question. Admins are Critical, members Warning and guests Info.";

    public CheckCategory Category => CheckCategory.Users;

    public IReadOnlyList<string> RequiredPermissions => new[] { "AuditLog.Read.All" };

    // The user registration details report needs Entra ID P1 or P2.
    public LicenceRequirement RequiredLicence => LicenceRequirement.EntraIdP1;

    // Named like the user list in the Entra admin center. Findings are about the user itself, with no due date.
    public FindingLabels Labels { get; } = new()
    {
        ObjectDisplayName = "Display name",
        UserPrincipalName = "User principal name",
        ObjectId = "Object ID",
        SubObjectDisplayName = null,
        SubObjectType = null,
        DueDate = null,
    };

    public async Task<IReadOnlyList<Finding>> RunAsync(
        IDirectoryReader reader,
        CheckOptions options,
        CancellationToken cancellationToken = default)
    {
        var users = await reader.GetUserRegistrationsAsync(cancellationToken).ConfigureAwait(false);
        var findings = new List<Finding>();

        foreach (var user in users)
        {
            if (!user.IsMfaCapable)
            {
                findings.Add(MakeFinding(user));
            }
        }

        return findings;
    }

    /// <summary>The most recent update time in the user registration details report, so admins know how fresh it is.</summary>
    public async Task<DateTimeOffset?> GetDataUpdatedAsync(
        IDirectoryReader reader,
        CancellationToken cancellationToken = default)
    {
        var users = await reader.GetUserRegistrationsAsync(cancellationToken).ConfigureAwait(false);
        return users.Max(u => u.LastUpdatedDateTime);
    }

    private Finding MakeFinding(UserRegistration user)
    {
        var severity = user.IsAdmin
            ? Severity.Critical
            : user.UserType == UserType.Guest ? Severity.Info : Severity.Warning;

        var message = user.IsMfaRegistered
            ? "Registered MFA methods are not allowed by the current policy."
            : "Not registered for MFA.";

        if (user.UserType == UserType.Guest)
        {
            message += GuestNote;
        }

        return new Finding
        {
            CheckId = Id,
            Severity = severity,
            ObjectType = "User",
            ObjectId = user.Id,
            ObjectDisplayName = user.DisplayName,
            UserPrincipalName = user.UserPrincipalName,
            Message = message,
        };
    }
}
