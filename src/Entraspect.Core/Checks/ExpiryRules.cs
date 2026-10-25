namespace Entraspect.Core.Checks;

/// <summary>Where an end date stands relative to now, as worked out by <see cref="ExpiryRules.Evaluate"/>.</summary>
/// <param name="DaysUntil">
/// Whole UTC calendar days from now to the end date: 0 for today, negative once the date has passed.
/// </param>
/// <param name="IsExpired">True when the end date is before now. Ending exactly now is not expired.</param>
/// <param name="Window">
/// For an end date that has not passed: <see cref="Severity.Critical"/> within
/// <see cref="CheckOptions.ExpiryCriticalDays"/>, <see cref="Severity.Warning"/> within
/// <see cref="CheckOptions.ExpiryWarningDays"/>, otherwise null. Null for an expired end date, since what an expiry
/// means is up to the check.
/// </param>
public readonly record struct ExpiryStatus(int DaysUntil, bool IsExpired, Severity? Window)
{
    /// <summary>"expired today", "expired 5 days ago", "expires today" or "expires in 1 day".</summary>
    public string Phrase => IsExpired
        ? DaysUntil == 0 ? "expired today" : $"expired {ExpiryRules.FormatDays(-DaysUntil)} ago"
        : DaysUntil == 0 ? "expires today" : $"expires in {ExpiryRules.FormatDays(DaysUntil)}";
}

/// <summary>Day counting and expiry windows shared by the checks that look at end dates.</summary>
public static class ExpiryRules
{
    public static ExpiryStatus Evaluate(DateTimeOffset end, DateTimeOffset now, CheckOptions options)
    {
        // Whole UTC calendar days, used for both severity and message so they always agree.
        var daysUntil = (end.UtcDateTime.Date - now.UtcDateTime.Date).Days;

        if (end < now)
        {
            return new ExpiryStatus(daysUntil, IsExpired: true, Window: null);
        }

        var window = daysUntil <= options.ExpiryCriticalDays
            ? Severity.Critical
            : daysUntil <= options.ExpiryWarningDays
                ? Severity.Warning
                : (Severity?)null;

        return new ExpiryStatus(daysUntil, IsExpired: false, window);
    }

    public static string FormatDays(int days) => days == 1 ? "1 day" : $"{days} days";
}
