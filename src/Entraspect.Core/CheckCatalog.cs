using System.Collections.Generic;

namespace Entraspect.Core;

/// <summary>
/// The single list of every check in the app, in navigation order within each category.
/// Later specs register new checks here and nowhere else.
/// </summary>
public static class CheckCatalog
{
    /// <summary>
    /// Longest allowed <see cref="ICheck.Title"/>. The title is the navigation label, so keep it short; the section
    /// already names the object type (for example "Expiring credentials" under App registrations).
    /// </summary>
    public const int MaxTitleLength = 26;

    public static IReadOnlyList<ICheck> All(TimeProvider timeProvider) =>
        new ICheck[]
        {
            new Checks.GroupNoMembersCheck(timeProvider),
            new Checks.GroupNoOwnersCheck(timeProvider),
            new Checks.AppCredentialExpiryCheck(timeProvider),
            new Checks.SamlSigningCertificateExpiryCheck(timeProvider),
        };
}
