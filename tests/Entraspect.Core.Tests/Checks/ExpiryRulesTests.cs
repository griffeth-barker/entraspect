using Entraspect.Core.Checks;

namespace Entraspect.Core.Tests.Checks;

public sealed class ExpiryRulesTests
{
    // Midday, so elapsed days and UTC calendar days differ for end dates with a time of day.
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly CheckOptions Defaults = new();

    [Theory]
    [InlineData(31, null)]
    [InlineData(30, Severity.Warning)]
    [InlineData(8, Severity.Warning)]
    [InlineData(7, Severity.Critical)]
    [InlineData(1, Severity.Critical)]
    [InlineData(0, Severity.Critical)]
    public void Window_follows_the_default_options(int days, Severity? window)
    {
        var status = ExpiryRules.Evaluate(Now.AddDays(days), Now, Defaults);

        Assert.Equal(new ExpiryStatus(days, IsExpired: false, window), status);
    }

    [Fact]
    public void Custom_windows_are_honored()
    {
        var options = new CheckOptions { ExpiryWarningDays = 10, ExpiryCriticalDays = 3 };

        Assert.Equal(Severity.Critical, ExpiryRules.Evaluate(Now.AddDays(3), Now, options).Window);
        Assert.Equal(Severity.Warning, ExpiryRules.Evaluate(Now.AddDays(4), Now, options).Window);
        Assert.Equal(Severity.Warning, ExpiryRules.Evaluate(Now.AddDays(10), Now, options).Window);
        Assert.Null(ExpiryRules.Evaluate(Now.AddDays(11), Now, options).Window);
    }

    [Fact]
    public void Past_end_date_is_expired_with_no_window()
    {
        var status = ExpiryRules.Evaluate(Now.AddDays(-5), Now, Defaults);

        Assert.Equal(new ExpiryStatus(-5, IsExpired: true, Window: null), status);
        Assert.Equal("expired 5 days ago", status.Phrase);
    }

    [Fact]
    public void Ending_exactly_now_is_not_expired()
    {
        var status = ExpiryRules.Evaluate(Now, Now, Defaults);

        Assert.False(status.IsExpired);
        Assert.Equal(0, status.DaysUntil);
        Assert.Equal("expires today", status.Phrase);
    }

    [Fact]
    public void Earlier_today_is_expired_today()
    {
        var status = ExpiryRules.Evaluate(Now.AddHours(-6), Now, Defaults);

        Assert.True(status.IsExpired);
        Assert.Equal(0, status.DaysUntil);
        Assert.Equal("expired today", status.Phrase);
    }

    [Theory]
    [InlineData(-3, 7)]
    [InlineData(6, 7)]
    public void Days_are_whole_utc_calendar_days(int hours, int expectedDays)
    {
        var status = ExpiryRules.Evaluate(Now.AddDays(7).AddHours(hours), Now, Defaults);

        Assert.Equal(expectedDays, status.DaysUntil);
    }

    [Fact]
    public void Days_are_counted_in_utc_not_the_end_date_offset()
    {
        // 2026-01-23 01:00 at +03:00 is 2026-01-22 22:00 UTC: 7 UTC calendar days away, not 8.
        var end = new DateTimeOffset(2026, 1, 23, 1, 0, 0, TimeSpan.FromHours(3));

        Assert.Equal(7, ExpiryRules.Evaluate(end, Now, Defaults).DaysUntil);
    }

    [Theory]
    [InlineData(1, "expires in 1 day")]
    [InlineData(2, "expires in 2 days")]
    [InlineData(-1, "expired 1 day ago")]
    public void Phrase_uses_singular_for_one_day(int days, string phrase)
    {
        Assert.Equal(phrase, ExpiryRules.Evaluate(Now.AddDays(days), Now, Defaults).Phrase);
    }
}
