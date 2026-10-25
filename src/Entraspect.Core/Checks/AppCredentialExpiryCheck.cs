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

    // Named like the app registration and its Certificates & secrets page in the Entra admin center.
    public FindingLabels Labels { get; } = new()
    {
        ObjectDisplayName = "Display name",
        ObjectId = "Object ID",
        SubObjectDisplayName = "Credential",
        SubObjectType = "Type",
        DueDate = "Expires",
    };

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

        var status = ExpiryRules.Evaluate(end.Value, now, options);

        if (status.IsExpired)
        {
            var hasValidReplacement = app.Credentials.Any(c =>
                c != credential &&
                c.Type == credential.Type &&
                c.EndDateTime is not null &&
                c.EndDateTime.Value > now);

            var message = hasValidReplacement
                ? $"{credential.Type} '{credential.DisplayName}' {status.Phrase}; a newer credential exists, consider removing this one."
                : $"{credential.Type} '{credential.DisplayName}' {status.Phrase}.";

            var severity = hasValidReplacement ? Severity.Info : Severity.Critical;

            return MakeFinding(app, credential, severity, message, end.Value);
        }

        return status.Window switch
        {
            Severity.Critical => MakeFinding(
                app,
                credential,
                Severity.Critical,
                $"{credential.Type} '{credential.DisplayName}' {status.Phrase} (within critical window).",
                end.Value),
            Severity.Warning => MakeFinding(
                app,
                credential,
                Severity.Warning,
                $"{credential.Type} '{credential.DisplayName}' {status.Phrase}.",
                end.Value),
            _ => null,
        };
    }

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
