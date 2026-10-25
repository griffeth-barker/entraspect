namespace Entraspect.Core.Checks;

/// <summary>Rules shared by the guest account checks.</summary>
internal static class GuestCheckRules
{
    public const string ObjectType = "User";

    /// <summary>
    /// Columns named like the user list in the Entra admin center, plus the check's own date, item and value columns.
    /// A null label hides that column.
    /// </summary>
    public static FindingLabels Labels(string? dueDate, string? subObjectDisplayName = null, string? value = null) =>
        new()
        {
            ObjectDisplayName = "Display name",
            UserPrincipalName = "User principal name",
            ObjectId = "Object ID",
            SubObjectDisplayName = subObjectDisplayName,
            SubObjectType = null,
            DueDate = dueDate,
            Value = value,
        };

    public static bool IsGuest(DirectoryUser user) => user.UserType == UserType.Guest;

    /// <summary>UTC calendar days from <paramref name="then"/> to <paramref name="now"/>, as the other checks count them.</summary>
    public static int DaysSince(DateTimeOffset then, DateTimeOffset now) =>
        (now.UtcDateTime.Date - then.UtcDateTime.Date).Days;

    public static string FormatDays(int days) => days == 1 ? "1 day" : $"{days} days";

    /// <summary>The invitation state as the Invitation column shows it; null when Graph has none or one unknown here.</summary>
    public static string? InvitationText(ExternalUserState? state) =>
        state switch
        {
            ExternalUserState.Accepted => "Accepted",
            ExternalUserState.PendingAcceptance => "Pending",
            _ => null,
        };

    public static Finding MakeFinding(
        string checkId,
        DirectoryUser user,
        Severity severity,
        string message,
        DateTimeOffset? dueDate = null,
        string? subObjectDisplayName = null,
        string? value = null) =>
        new()
        {
            CheckId = checkId,
            Severity = severity,
            ObjectType = ObjectType,
            ObjectId = user.Id,
            ObjectDisplayName = user.DisplayName,
            UserPrincipalName = user.UserPrincipalName,
            Message = message,
            DueDate = dueDate,
            SubObjectDisplayName = subObjectDisplayName,
            Value = value,
        };
}
