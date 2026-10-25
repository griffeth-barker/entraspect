namespace Entraspect.Core;

/// <summary>
/// The message shown for a failed check. A tenant without the licence a check needs gets a plain sentence instead of
/// the raw Graph error.
/// </summary>
public static class CheckFailureMessage
{
    public const string LicenceRequired = "This check needs an Entra ID P1 or P2 licence.";

    // Graph error codes that mean the tenant lacks an Entra ID P1 or P2 licence.
    private static readonly string[] PremiumLicenceErrorCodes =
    [
        "Authentication_RequestFromNonPremiumTenantOrB2CTenant",
    ];

    /// <summary>Whether <paramref name="errorCode"/> says the tenant lacks an Entra ID P1 or P2 licence.</summary>
    public static bool IsLicenceRequired(string? errorCode) =>
        errorCode is not null &&
        PremiumLicenceErrorCodes.Contains(errorCode, StringComparer.OrdinalIgnoreCase);

    public static string For(CheckResult result) =>
        IsLicenceRequired(result.ErrorCode)
            ? LicenceRequired
            : result.ErrorMessage ?? "Unknown error.";
}
