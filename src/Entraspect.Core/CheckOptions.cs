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

    /// <summary>Inactive guests reports guests with no successful sign-in for at least this many UTC calendar days.</summary>
    public int GuestInactiveDays { get; init; } = 90;

    public const int MinGuestInactiveDays = 30;

    public const int MaxGuestInactiveDays = 365;

    /// <summary>Pending guest invitations reports invitations not accepted after this many UTC calendar days.</summary>
    public int GuestPendingInviteDays { get; init; } = 30;

    public const int MinGuestPendingInviteDays = 1;

    /// <summary>Inactive users reports members with no successful sign-in for at least this many UTC calendar days.</summary>
    public int UserInactiveDays { get; init; } = 90;

    public const int MinUserInactiveDays = 30;

    public const int MaxUserInactiveDays = 365;

    /// <summary>Returns null if <paramref name="options"/> is valid, otherwise what is wrong, for Settings.</summary>
    public static string? Validate(CheckOptions options) =>
        ExpiryError(options) ?? NewGroupGraceError(options) ?? ReportOnlyError(options) ??
        SignInLookbackError(options) ?? GuestInactiveError(options) ?? GuestPendingInviteError(options) ??
        UserInactiveError(options);

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

    private static string? GuestInactiveError(CheckOptions options) =>
        options.GuestInactiveDays is < MinGuestInactiveDays or > MaxGuestInactiveDays
            ? $"Inactive guest days must be between {MinGuestInactiveDays} and {MaxGuestInactiveDays}."
            : null;

    private static string? GuestPendingInviteError(CheckOptions options) =>
        options.GuestPendingInviteDays < MinGuestPendingInviteDays
            ? $"Pending invitation days must be at least {MinGuestPendingInviteDays}."
            : null;

    private static string? UserInactiveError(CheckOptions options) =>
        options.UserInactiveDays is < MinUserInactiveDays or > MaxUserInactiveDays
            ? $"Inactive user days must be between {MinUserInactiveDays} and {MaxUserInactiveDays}."
            : null;

    /// <summary>
    /// Returns a valid copy of <paramref name="options"/>, or the defaults if it is null. Each rule only resets the
    /// options it covers, so add new options here with their own rule. Expiry: <c>0 &lt;= ExpiryCriticalDays &lt;=
    /// ExpiryWarningDays</c>, otherwise both expiry values fall back to their defaults. New group grace:
    /// <c>NewGroupGraceDays &gt;= 0</c>, otherwise it falls back to its default. Report-only warning:
    /// <c>ReportOnlyWarningDays &gt;= 0</c>, otherwise it falls back to its default. Sign-in lookback:
    /// <c>1 &lt;= SignInLookbackDays &lt;= 30</c>, otherwise it falls back to its default. Inactive guests:
    /// <c>30 &lt;= GuestInactiveDays &lt;= 365</c>, otherwise it falls back to its default. Pending invitations:
    /// <c>GuestPendingInviteDays &gt;= 1</c>, otherwise it falls back to its default. Inactive users:
    /// <c>30 &lt;= UserInactiveDays &lt;= 365</c>, otherwise it falls back to its default.
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
        var guestInactiveValid = GuestInactiveError(options) is null;
        var guestPendingInviteValid = GuestPendingInviteError(options) is null;
        var userInactiveValid = UserInactiveError(options) is null;

        return new CheckOptions
        {
            ExpiryWarningDays = expiryValid ? options.ExpiryWarningDays : defaults.ExpiryWarningDays,
            ExpiryCriticalDays = expiryValid ? options.ExpiryCriticalDays : defaults.ExpiryCriticalDays,
            NewGroupGraceDays = graceValid ? options.NewGroupGraceDays : defaults.NewGroupGraceDays,
            ReportOnlyWarningDays = reportOnlyValid ? options.ReportOnlyWarningDays : defaults.ReportOnlyWarningDays,
            SignInLookbackDays = lookbackValid ? options.SignInLookbackDays : defaults.SignInLookbackDays,
            GuestInactiveDays = guestInactiveValid ? options.GuestInactiveDays : defaults.GuestInactiveDays,
            GuestPendingInviteDays =
                guestPendingInviteValid ? options.GuestPendingInviteDays : defaults.GuestPendingInviteDays,
            UserInactiveDays = userInactiveValid ? options.UserInactiveDays : defaults.UserInactiveDays,
        };
    }
}
