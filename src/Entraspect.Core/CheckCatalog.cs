using System.Collections.Generic;

namespace Entraspect.Core;

/// <summary>
/// The single list of every check in the app, in navigation order within each category.
/// Later specs register new checks here and nowhere else.
/// </summary>
public static class CheckCatalog
{
    public static IReadOnlyList<ICheck> All(TimeProvider timeProvider) =>
        new ICheck[]
        {
            new Checks.AppCredentialExpiryCheck(timeProvider),
        };
}
