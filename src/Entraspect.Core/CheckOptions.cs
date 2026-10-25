namespace Entraspect.Core;

public sealed class CheckOptions
{
    public int ExpiryWarningDays { get; init; } = 30;

    public int ExpiryCriticalDays { get; init; } = 7;

    /// <summary>Groups created fewer than this many UTC calendar days ago are skipped by the group checks.</summary>
    public int NewGroupGraceDays { get; init; } = 7;

    /// <summary>
    /// Report-only Conditional Access policies unchanged for at least this many UTC calendar days are a Warning.
    /// </summary>
    public int ReportOnlyWarningDays { get; init; } = 30;

    /// <summary>How many days of sign-in logs checks read, from 1 to 30. Longer periods are slower in busy tenants.</summary>
    public int SignInLookbackDays { get; init; } = 7;

    public const int MinSignInLookbackDays = 1;

    public const int MaxSignInLookbackDays = 30;

    /// <summary>Returns null if <paramref name="options"/> is valid, otherwise what is wrong, for Settings.</summary>
    public static string? Validate(CheckOptions options) =>
        ExpiryError(options) ?? NewGroupGraceError(options) ?? ReportOnlyError(options) ??
        SignInLookbackError(options);

    private static string? ExpiryError(CheckOptions options) =>
        options.ExpiryCriticalDays < 0
            ? "Critical days can't be negative."
            : options.ExpiryCriticalDays > options.ExpiryWarningDays
                ? "Critical days must be less than or equal to warning days."
                : null;

    private static string? NewGroupGraceError(CheckOptions options) =>
        options.NewGroupGraceDays < 0 ? "New group grace days can't be negative." : null;

    private static string? ReportOnlyError(CheckOptions options) =>
        options.ReportOnlyWarningDays < 0 ? "Report-only warning days can't be negative." : null;

    private static string? SignInLookbackError(CheckOptions options) =>
        options.SignInLookbackDays is < MinSignInLookbackDays or > MaxSignInLookbackDays
            ? $"Sign-in lookback days must be between {MinSignInLookbackDays} and {MaxSignInLookbackDays}."
            : null;

    /// <summary>
    /// Returns a valid copy of <paramref name="options"/>, or the defaults if it is null. Each rule only resets the
    /// options it covers, so add new options here with their own rule. Expiry: <c>0 &lt;= ExpiryCriticalDays &lt;=
    /// ExpiryWarningDays</c>, otherwise both expiry values fall back to their defaults. New group grace:
    /// <c>NewGroupGraceDays &gt;= 0</c>, otherwise it falls back to its default. Report-only warning:
    /// <c>ReportOnlyWarningDays &gt;= 0</c>, otherwise it falls back to its default. Sign-in lookback:
    /// <c>1 &lt;= SignInLookbackDays &lt;= 30</c>, otherwise it falls back to its default.
    /// </summary>
    public static CheckOptions Normalize(CheckOptions? options)
    {
        var defaults = new CheckOptions();
        if (options is null)
        {
            return defaults;
        }

        var expiryValid = ExpiryError(options) is null;
        var graceValid = NewGroupGraceError(options) is null;
        var reportOnlyValid = ReportOnlyError(options) is null;
        var lookbackValid = SignInLookbackError(options) is null;

        return new CheckOptions
        {
            ExpiryWarningDays = expiryValid ? options.ExpiryWarningDays : defaults.ExpiryWarningDays,
            ExpiryCriticalDays = expiryValid ? options.ExpiryCriticalDays : defaults.ExpiryCriticalDays,
            NewGroupGraceDays = graceValid ? options.NewGroupGraceDays : defaults.NewGroupGraceDays,
            ReportOnlyWarningDays = reportOnlyValid ? options.ReportOnlyWarningDays : defaults.ReportOnlyWarningDays,
            SignInLookbackDays = lookbackValid ? options.SignInLookbackDays : defaults.SignInLookbackDays,
        };
    }
}
