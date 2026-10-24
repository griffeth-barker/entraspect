using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Entraspect.Core.Checks;

public sealed class AppCredentialExpiryCheck : ICheck
{
    private readonly TimeProvider _timeProvider;

    public AppCredentialExpiryCheck(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public string Id => "app-credential-expiry";

    public string Title => "Expiring credentials";

    public string Description =>
        "Surfaces app registration client secrets and certificates that are expired or expire soon.";

    public CheckCategory Category => CheckCategory.AppRegistrations;

    public IReadOnlyList<string> RequiredPermissions => new[] { "Application.Read.All" };

    public async Task<IReadOnlyList<Finding>> RunAsync(
        IDirectoryReader reader,
        CheckOptions options,
        CancellationToken cancellationToken = default)
    {
        var apps = await reader.GetAppRegistrationsAsync(cancellationToken).ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow();
        var findings = new List<Finding>();

        foreach (var app in apps)
        {
            foreach (var credential in app.Credentials)
            {
                var finding = Evaluate(app, credential, options, now);
                if (finding is not null)
                {
                    findings.Add(finding);
                }
            }
        }

        return findings;
    }

    private Finding? Evaluate(
        AppRegistration app,
        AppCredential credential,
        CheckOptions options,
        DateTimeOffset now)
    {
        var end = credential.EndDateTime;
        if (end is null)
        {
            return null;
        }

        // Whole UTC calendar days, used for both severity and message so they always agree.
        var daysUntil = (end.Value.UtcDateTime.Date - now.UtcDateTime.Date).Days;

        if (end.Value < now)
        {
            var hasValidReplacement = app.Credentials.Any(c =>
                c != credential &&
                c.Type == credential.Type &&
                c.EndDateTime is not null &&
                c.EndDateTime.Value > now);

            var expired = daysUntil == 0 ? "expired today" : $"expired {FormatDays(-daysUntil)} ago";
            var message = hasValidReplacement
                ? $"{credential.Type} '{credential.DisplayName}' {expired}; a newer credential exists, consider removing this one."
                : $"{credential.Type} '{credential.DisplayName}' {expired}.";

            var severity = hasValidReplacement ? Severity.Info : Severity.Critical;

            return MakeFinding(app, credential, severity, message, end.Value);
        }

        var expires = daysUntil == 0 ? "expires today" : $"expires in {FormatDays(daysUntil)}";
        if (daysUntil <= options.ExpiryCriticalDays)
        {
            var message = $"{credential.Type} '{credential.DisplayName}' {expires} (within critical window).";
            return MakeFinding(app, credential, Severity.Critical, message, end.Value);
        }

        if (daysUntil <= options.ExpiryWarningDays)
        {
            var message = $"{credential.Type} '{credential.DisplayName}' {expires}.";
            return MakeFinding(app, credential, Severity.Warning, message, end.Value);
        }

        return null;
    }

    private static string FormatDays(int days) => days == 1 ? "1 day" : $"{days} days";

    private Finding MakeFinding(
        AppRegistration app,
        AppCredential credential,
        Severity severity,
        string message,
        DateTimeOffset dueDate) =>
        new()
        {
            CheckId = Id,
            Severity = severity,
            ObjectType = "Application",
            ObjectId = app.Id,
            ObjectDisplayName = app.DisplayName,
            Message = message,
            DueDate = dueDate,
            SubObjectType = credential.Type.ToString(),
            SubObjectDisplayName = credential.DisplayName,
        };
}
