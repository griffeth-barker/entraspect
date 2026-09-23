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

    public string Title => "Expiring app registration credentials";

    public string Description =>
        "Surfaces app registration client secrets and certificates that are expired or expire soon.";

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

        if (end.Value < now)
        {
            var days = (int)(now - end.Value).TotalDays;
            var hasValidReplacement = app.Credentials.Any(c =>
                c != credential &&
                c.Type == credential.Type &&
                c.EndDateTime is not null &&
                c.EndDateTime.Value > now);

            var message = hasValidReplacement
                ? $"{credential.Type} '{credential.DisplayName}' expired {days} days ago; a newer credential exists, consider removing this one."
                : $"{credential.Type} '{credential.DisplayName}' expired {days} days ago.";

            var severity = hasValidReplacement ? Severity.Info : Severity.Critical;

            return MakeFinding(app, credential, severity, message, end.Value);
        }

        var daysUntil = (end.Value - now).TotalDays;
        if (daysUntil <= options.ExpiryCriticalDays)
        {
            var message = $"{credential.Type} '{credential.DisplayName}' expires in {(int)daysUntil} days (within critical window).";
            return MakeFinding(app, credential, Severity.Critical, message, end.Value);
        }

        if (daysUntil <= options.ExpiryWarningDays)
        {
            var message = $"{credential.Type} '{credential.DisplayName}' expires in {(int)daysUntil} days.";
            return MakeFinding(app, credential, Severity.Warning, message, end.Value);
        }

        return null;
    }

    private static Finding MakeFinding(
        AppRegistration app,
        AppCredential credential,
        Severity severity,
        string message,
        DateTimeOffset dueDate) =>
        new()
        {
            CheckId = "app-credential-expiry",
            Severity = severity,
            ObjectType = "Application",
            ObjectId = app.Id,
            ObjectDisplayName = app.DisplayName,
            Message = message,
            DueDate = dueDate,
        };
}
