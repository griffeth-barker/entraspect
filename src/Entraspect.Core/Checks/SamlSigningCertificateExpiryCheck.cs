using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Entraspect.Core.Checks;

public sealed class SamlSigningCertificateExpiryCheck : ICheck
{
    private const string NewerCertificateHint =
        " A newer certificate exists but is not active. Make it active and give it to the service provider.";

    private readonly TimeProvider _timeProvider;

    public SamlSigningCertificateExpiryCheck(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public string Id => "saml-signing-certificate-expiry";

    public string Title => "Expiring SAML certificates";

    public string Description =>
        "Surfaces SAML single sign-on enterprise apps whose active token signing certificate is expired or expires soon.";

    public CheckCategory Category => CheckCategory.EnterpriseApps;

    public IReadOnlyList<string> RequiredPermissions => new[] { "Application.Read.All" };

    // Named like the enterprise app and the SAML Certificates section of its Single sign-on page in the Entra admin
    // center. Every finding is about a signing certificate, so the type column is hidden.
    public FindingLabels Labels { get; } = new()
    {
        ObjectDisplayName = "Display name",
        ObjectId = "Object ID",
        SubObjectDisplayName = "Certificate",
        SubObjectType = null,
        DueDate = "Expires",
    };

    public async Task<IReadOnlyList<Finding>> RunAsync(
        IDirectoryReader reader,
        CheckOptions options,
        CancellationToken cancellationToken = default)
    {
        var apps = await reader.GetEnterpriseAppsAsync(cancellationToken).ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow();
        var findings = new List<Finding>();

        foreach (var app in apps.Where(IsSaml))
        {
            Evaluate(app, options, now, findings);
        }

        return findings;
    }

    private static bool IsSaml(EnterpriseApp app) =>
        string.Equals(app.PreferredSingleSignOnMode, "saml", StringComparison.OrdinalIgnoreCase);

    private void Evaluate(EnterpriseApp app, CheckOptions options, DateTimeOffset now, List<Finding> findings)
    {
        var active = string.IsNullOrEmpty(app.PreferredTokenSigningKeyThumbprint)
            ? null
            : app.SigningCertificates.FirstOrDefault(c => string.Equals(
                c.Thumbprint, app.PreferredTokenSigningKeyThumbprint, StringComparison.OrdinalIgnoreCase));

        if (active is null)
        {
            findings.Add(MakeFinding(app, null, Severity.Warning, "SAML app has no active signing certificate."));
        }
        else if (EvaluateActive(app, active, options, now) is { } finding)
        {
            findings.Add(finding);
        }

        foreach (var certificate in app.SigningCertificates.Where(c => c != active))
        {
            if (certificate.EndDateTime is { } end && ExpiryRules.Evaluate(end, now, options) is { IsExpired: true } status)
            {
                findings.Add(MakeFinding(
                    app,
                    certificate,
                    Severity.Info,
                    $"Inactive signing certificate {status.Phrase}; consider removing it."));
            }
        }
    }

    private Finding? EvaluateActive(
        EnterpriseApp app,
        SigningCertificate active,
        CheckOptions options,
        DateTimeOffset now)
    {
        if (active.EndDateTime is not { } end)
        {
            return null;
        }

        var status = ExpiryRules.Evaluate(end, now, options);
        string message;
        Severity severity;

        if (status.IsExpired)
        {
            severity = Severity.Critical;
            message = $"Active signing certificate {status.Phrase}. SAML sign-in to this app may be failing.";
        }
        else if (status.Window == Severity.Critical)
        {
            severity = Severity.Critical;
            message = $"Active signing certificate {status.Phrase} (within critical window).";
        }
        else if (status.Window == Severity.Warning)
        {
            severity = Severity.Warning;
            message = $"Active signing certificate {status.Phrase}.";
        }
        else
        {
            return null;
        }

        // The severity stays: someone still has to activate the newer certificate and give it to the service provider.
        var hasNewer = app.SigningCertificates.Any(c =>
            c != active &&
            c.EndDateTime is { } otherEnd &&
            otherEnd > end &&
            otherEnd > now);

        return MakeFinding(app, active, severity, hasNewer ? message + NewerCertificateHint : message);
    }

    private Finding MakeFinding(
        EnterpriseApp app,
        SigningCertificate? certificate,
        Severity severity,
        string message) =>
        new()
        {
            CheckId = Id,
            // A disabled app can't be signed in to, so nothing is urgent; the finding is kept for clean-up.
            Severity = app.AccountEnabled ? severity : Severity.Info,
            ObjectType = "EnterpriseApp",
            ObjectId = app.Id,
            ObjectDisplayName = app.DisplayName,
            Message = app.AccountEnabled ? message : "App is disabled. " + message,
            DueDate = certificate?.EndDateTime,
            SubObjectType = certificate is null ? null : "Signing certificate",
            SubObjectDisplayName = certificate is null ? null : CertificateName(certificate),
        };

    private static string CertificateName(SigningCertificate certificate) =>
        !string.IsNullOrWhiteSpace(certificate.DisplayName) ? certificate.DisplayName
        : !string.IsNullOrEmpty(certificate.Thumbprint) ? certificate.Thumbprint
        : certificate.KeyId.ToString();
}
