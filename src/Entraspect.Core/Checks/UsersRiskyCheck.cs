using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Entraspect.Core.Checks;

public sealed class UsersRiskyCheck : ICheck
{
    public const string CompromisedMessage =
        "Confirmed compromised. Reset the password, revoke sessions and review the account's activity.";

    public string Id => "users-risky";

    public string Title => "Risky users";

    public string Description =>
        "Members and guests that Microsoft Entra ID Protection rates at risk, or that an admin confirmed compromised. " +
        "Users whose risk was remediated, dismissed or confirmed safe aren't reported. Investigate and act in the Entra " +
        "admin center, under ID Protection > Risky users; entraspect never confirms or dismisses risk.";

    public CheckCategory Category => CheckCategory.Users;

    // IdentityRiskyUser.Read.All: the risky users report.
    public IReadOnlyList<string> RequiredPermissions => new[] { "IdentityRiskyUser.Read.All" };

    // ID Protection needs Entra ID P2.
    public LicenceRequirement RequiredLicence => LicenceRequirement.EntraIdP2;

    public FindingLabels Labels { get; } = UserCheckRules.Labels(dueDate: "Risk updated", value: "Risk level");

    public async Task<IReadOnlyList<Finding>> RunAsync(
        IDirectoryReader reader,
        CheckOptions options,
        CancellationToken cancellationToken = default)
    {
        var findings = new List<Finding>();

        foreach (var user in await reader.GetRiskyUsersAsync(cancellationToken).ConfigureAwait(false))
        {
            if (user.IsDeleted || Evaluate(user) is not (var severity, var message))
            {
                continue;
            }

            findings.Add(new Finding
            {
                CheckId = Id,
                Severity = severity,
                ObjectType = UserCheckRules.ObjectType,
                ObjectId = user.Id,
                ObjectDisplayName = user.DisplayName,
                UserPrincipalName = user.UserPrincipalName,
                Message = message,
                DueDate = user.RiskLastUpdated,
                Value = user.RiskLevel.ToString(),
            });
        }

        return findings;
    }

    /// <summary>The finding for a user at risk or confirmed compromised, or null for any other state.</summary>
    private static (Severity Severity, string Message)? Evaluate(RiskyUser user)
    {
        if (user.RiskState == RiskState.ConfirmedCompromised)
        {
            return (Severity.Critical, CompromisedMessage);
        }

        if (user.RiskState != RiskState.AtRisk)
        {
            return null;
        }

        var detail = DetailText(user.RiskDetail);
        return user.RiskLevel switch
        {
            RiskLevel.High => (Severity.Critical, AtRisk("High") + detail),
            RiskLevel.Medium => (Severity.Warning, AtRisk("Medium") + detail),
            RiskLevel.Low => (Severity.Info, AtRisk("Low") + detail),
            _ => (Severity.Info, "At risk; the risk level isn't shown." + detail),
        };
    }

    private static string AtRisk(string level) => $"At risk ({level}) according to Microsoft Entra ID Protection.";

    /// <summary>" Risk detail: VALUE." or empty when Graph gives no detail worth showing.</summary>
    private static string DetailText(string detail) =>
        string.IsNullOrWhiteSpace(detail) ||
        detail.Trim().ToUpperInvariant() is "NONE" or "HIDDEN" or "UNKNOWNFUTUREVALUE"
            ? string.Empty
            : $" Risk detail: {detail.Trim()}.";
}
