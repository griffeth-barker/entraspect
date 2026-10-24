namespace Entraspect.Core;

public sealed class CheckOptions
{
    public int ExpiryWarningDays { get; init; } = 30;

    public int ExpiryCriticalDays { get; init; } = 7;

    /// <summary>Returns null if <paramref name="options"/> is valid, otherwise what is wrong, for Settings.</summary>
    public static string? Validate(CheckOptions options) => ExpiryError(options);

    private static string? ExpiryError(CheckOptions options) =>
        options.ExpiryCriticalDays < 0
            ? "Critical days can't be negative."
            : options.ExpiryCriticalDays > options.ExpiryWarningDays
                ? "Critical days must be less than or equal to warning days."
                : null;

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

        var expiryValid = ExpiryError(options) is null;

        return new CheckOptions
        {
            ExpiryWarningDays = expiryValid ? options.ExpiryWarningDays : defaults.ExpiryWarningDays,
            ExpiryCriticalDays = expiryValid ? options.ExpiryCriticalDays : defaults.ExpiryCriticalDays,
        };
    }
}
