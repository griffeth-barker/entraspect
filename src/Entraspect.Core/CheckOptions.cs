namespace Entraspect.Core;

public sealed class CheckOptions
{
    public int ExpiryWarningDays { get; init; } = 30;

    public int ExpiryCriticalDays { get; init; } = 7;

    /// <summary>
    /// Returns a valid copy of <paramref name="options"/>, or the defaults if it is null. Each rule only resets the
    /// options it covers, so add new options here with their own rule. Expiry: <c>0 &lt;= ExpiryCriticalDays &lt;=
    /// ExpiryWarningDays</c>, otherwise both expiry values fall back to their defaults.
    /// </summary>
    public static CheckOptions Normalize(CheckOptions? options)
    {
        var defaults = new CheckOptions();
        if (options is null)
        {
            return defaults;
        }

        var expiryValid = options.ExpiryCriticalDays >= 0 && options.ExpiryCriticalDays <= options.ExpiryWarningDays;

        return new CheckOptions
        {
            ExpiryWarningDays = expiryValid ? options.ExpiryWarningDays : defaults.ExpiryWarningDays,
            ExpiryCriticalDays = expiryValid ? options.ExpiryCriticalDays : defaults.ExpiryCriticalDays,
        };
    }
}
