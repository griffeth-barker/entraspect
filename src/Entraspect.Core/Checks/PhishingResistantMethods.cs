using System.Collections.Generic;
using System.Linq;

namespace Entraspect.Core.Checks;

/// <summary>
/// The <c>methodsRegistered</c> values of the user registration details report that are phishing-resistant: device-bound
/// passkeys (including Microsoft Authenticator and Windows Hello passkeys), FIDO2 security keys, Windows Hello for
/// Business, Platform Credential for macOS and certificate-based authentication. Any other value, including one
/// unknown here, isn't phishing-resistant.
/// </summary>
/// <remarks>
/// Read 2026-10-25. The Graph docs, https://learn.microsoft.com/en-us/graph/api/resources/userregistrationdetails, and
/// the authentication methods activity docs,
/// https://learn.microsoft.com/en-us/entra/identity/authentication/howto-authentication-methods-activity, don't list
/// the values: only <c>passKeyDeviceBound</c> appears there. The others come from observed report output. Sources
/// disagree on <c>fido2</c> and <c>fido2SecurityKey</c>, so both are kept. The report has one value for
/// certificate-based authentication, so it can't tell a single-factor certificate from a multifactor one.
/// </remarks>
public static class PhishingResistantMethods
{
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "passKeyDeviceBound",
        "passKeyDeviceBoundAuthenticator",
        "passKeyDeviceBoundWindowsHello",
        "fido2SecurityKey",
        "fido2",
        "windowsHelloForBusiness",
        "macOsSecureEnclaveKey",
        "certificateBasedAuthentication",
    };

    /// <summary>Whether any of <paramref name="methodsRegistered"/> is phishing-resistant, ignoring case.</summary>
    public static bool AnyIn(IEnumerable<string> methodsRegistered) => methodsRegistered.Any(All.Contains);
}
