using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Entraspect.Core.Checks;

public sealed class RolesAdminsNoPhishingResistantMfaCheck : ICheck
{
    public string Id => "roles-admins-no-phishing-resistant-mfa";

    public string Title => "No phishing-resistant MFA";

    public string Description =>
        "Enabled admins who haven't registered a phishing-resistant method: a passkey, a FIDO2 security key, Windows " +
        "Hello for Business, Platform Credential for macOS or certificate-based authentication. Admins are users " +
        "holding a Tier 0 (Warning) or other privileged (Info) role, active, at any scope, directly or through a " +
        "role-assignable group. Admins without any MFA are also reported by MFA not registered. Needs Entra ID P1 or P2.";

    public CheckCategory Category => CheckCategory.RolesAndAdmins;

    // AuditLog.Read.All: the Authentication methods user registration details report.
    public IReadOnlyList<string> RequiredPermissions =>
        [.. AdminCheckRules.RequiredPermissions, "AuditLog.Read.All"];

    // The user registration details report needs Entra ID P1 or P2.
    public LicenceRequirement RequiredLicence => LicenceRequirement.EntraIdP1;

    public FindingLabels Labels { get; } = AdminCheckRules.Labels();

    public async Task<IReadOnlyList<Finding>> RunAsync(
        IDirectoryReader reader,
        CheckOptions options,
        CancellationToken cancellationToken = default)
    {
        var admins = await AdminCheckRules.GetAdminsAsync(reader, cancellationToken).ConfigureAwait(false);
        var registrations = await reader.GetUserRegistrationsAsync(cancellationToken).ConfigureAwait(false);

        var byId = registrations
            .GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var findings = new List<Finding>();

        // The report never lists disabled users, and Disabled users with access reports disabled admins.
        foreach (var admin in admins.Where(a => a.User.AccountEnabled))
        {
            if (!byId.TryGetValue(admin.User.Id, out var registration))
            {
                findings.Add(AdminCheckRules.MakeFinding(
                    Id,
                    admin,
                    Severity.Info,
                    "No registration details for this admin."));
            }
            else if (!PhishingResistantMethods.AnyIn(registration.MethodsRegistered))
            {
                findings.Add(AdminCheckRules.MakeFinding(
                    Id,
                    admin,
                    admin.Tier == RoleTier.Tier0 ? Severity.Warning : Severity.Info,
                    "No phishing-resistant method registered (passkey, security key, Windows Hello or certificate). " +
                    "Admins should sign in with one."));
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
}
