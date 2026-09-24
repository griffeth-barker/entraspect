namespace Entraspect.Core;

public sealed class CheckOptions
{
    public int ExpiryWarningDays { get; init; } = 30;

    public int ExpiryCriticalDays { get; init; } = 7;

    /// <summary>
    /// Returns a valid <see cref="CheckOptions"/>, enforcing <c>0 &lt;= ExpiryCriticalDays &lt;= ExpiryWarningDays</c>.
    /// If the supplied values break that rule, the defaults are returned.
    /// </summary>
    public static CheckOptions Normalize(int expiryWarningDays, int expiryCriticalDays)
    {
        var valid = expiryWarningDays >= 0 && expiryCriticalDays >= 0 && expiryCriticalDays <= expiryWarningDays;
        return valid
            ? new CheckOptions { ExpiryWarningDays = expiryWarningDays, ExpiryCriticalDays = expiryCriticalDays }
            : new CheckOptions();
    }
}
