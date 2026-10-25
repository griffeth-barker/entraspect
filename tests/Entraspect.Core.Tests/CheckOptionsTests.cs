namespace Entraspect.Core.Tests;

public sealed class CheckOptionsTests
{
    [Fact]
    public void Null_returns_defaults()
    {
        var options = CheckOptions.Normalize(null);

        Assert.Equal(30, options.ExpiryWarningDays);
        Assert.Equal(7, options.ExpiryCriticalDays);
        Assert.Equal(7, options.NewGroupGraceDays);
        Assert.Equal(30, options.ReportOnlyWarningDays);
        Assert.Equal(7, options.SignInLookbackDays);
        Assert.Equal(90, options.GuestInactiveDays);
        Assert.Equal(30, options.GuestPendingInviteDays);
        Assert.Equal(90, options.UserInactiveDays);
    }

    [Theory]
    [InlineData(30, 7)]
    [InlineData(0, 0)]
    [InlineData(10, 10)]
    public void Valid_expiry_values_are_kept(int warning, int critical)
    {
        var options = CheckOptions.Normalize(new CheckOptions { ExpiryWarningDays = warning, ExpiryCriticalDays = critical });

        Assert.Equal(warning, options.ExpiryWarningDays);
        Assert.Equal(critical, options.ExpiryCriticalDays);
    }

    [Theory]
    [InlineData(5, 10)]
    [InlineData(-5, -10)]
    [InlineData(-1, 0)]
    [InlineData(30, -1)]
    public void Invalid_expiry_values_fall_back_to_defaults(int warning, int critical)
    {
        var options = CheckOptions.Normalize(new CheckOptions { ExpiryWarningDays = warning, ExpiryCriticalDays = critical });

        Assert.Equal(30, options.ExpiryWarningDays);
        Assert.Equal(7, options.ExpiryCriticalDays);
    }

    [Theory]
    [InlineData(30, 7)]
    [InlineData(0, 0)]
    public void Validate_accepts_valid_expiry_values(int warning, int critical)
    {
        Assert.Null(CheckOptions.Validate(new CheckOptions { ExpiryWarningDays = warning, ExpiryCriticalDays = critical }));
    }

    [Theory]
    [InlineData(5, 10)]
    [InlineData(30, -1)]
    public void Validate_rejects_invalid_expiry_values(int warning, int critical)
    {
        Assert.NotNull(CheckOptions.Validate(new CheckOptions { ExpiryWarningDays = warning, ExpiryCriticalDays = critical }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    public void Valid_new_group_grace_days_are_kept_and_accepted(int days)
    {
        var options = new CheckOptions { NewGroupGraceDays = days };

        Assert.Equal(days, CheckOptions.Normalize(options).NewGroupGraceDays);
        Assert.Null(CheckOptions.Validate(options));
    }

    [Fact]
    public void Negative_new_group_grace_days_fall_back_to_default_and_are_rejected()
    {
        var options = new CheckOptions { ExpiryWarningDays = 40, ExpiryCriticalDays = 10, NewGroupGraceDays = -1 };

        var normalized = CheckOptions.Normalize(options);

        Assert.Equal(7, normalized.NewGroupGraceDays);
        Assert.Equal((40, 10), (normalized.ExpiryWarningDays, normalized.ExpiryCriticalDays));
        Assert.NotNull(CheckOptions.Validate(options));
    }

    [Fact]
    public void Invalid_expiry_values_keep_new_group_grace_days()
    {
        var options = CheckOptions.Normalize(new CheckOptions { ExpiryWarningDays = 5, ExpiryCriticalDays = 10, NewGroupGraceDays = 3 });

        Assert.Equal(3, options.NewGroupGraceDays);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    public void Valid_report_only_warning_days_are_kept_and_accepted(int days)
    {
        var options = new CheckOptions { ReportOnlyWarningDays = days };

        Assert.Equal(days, CheckOptions.Normalize(options).ReportOnlyWarningDays);
        Assert.Null(CheckOptions.Validate(options));
    }

    [Fact]
    public void Negative_report_only_warning_days_fall_back_to_default_and_are_rejected()
    {
        var options = new CheckOptions { NewGroupGraceDays = 3, ReportOnlyWarningDays = -1 };

        var normalized = CheckOptions.Normalize(options);

        Assert.Equal(30, normalized.ReportOnlyWarningDays);
        Assert.Equal(3, normalized.NewGroupGraceDays);
        Assert.NotNull(CheckOptions.Validate(options));
    }

    [Fact]
    public void Invalid_expiry_values_keep_report_only_warning_days()
    {
        var options = CheckOptions.Normalize(new CheckOptions { ExpiryWarningDays = 5, ExpiryCriticalDays = 10, ReportOnlyWarningDays = 0 });

        Assert.Equal(0, options.ReportOnlyWarningDays);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    public void Valid_sign_in_lookback_days_are_kept_and_accepted(int days)
    {
        var options = new CheckOptions { SignInLookbackDays = days };

        Assert.Equal(days, CheckOptions.Normalize(options).SignInLookbackDays);
        Assert.Null(CheckOptions.Validate(options));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public void Out_of_range_sign_in_lookback_days_fall_back_to_default_and_are_rejected(int days)
    {
        var options = new CheckOptions { ReportOnlyWarningDays = 3, SignInLookbackDays = days };

        var normalized = CheckOptions.Normalize(options);

        Assert.Equal(7, normalized.SignInLookbackDays);
        Assert.Equal(3, normalized.ReportOnlyWarningDays);
        Assert.Equal("Sign-in lookback days must be between 1 and 30.", CheckOptions.Validate(options));
    }

    [Theory]
    [InlineData(30)]
    [InlineData(365)]
    public void Valid_guest_inactive_days_are_kept_and_accepted(int days)
    {
        var options = new CheckOptions { GuestInactiveDays = days };

        Assert.Equal(days, CheckOptions.Normalize(options).GuestInactiveDays);
        Assert.Null(CheckOptions.Validate(options));
    }

    [Theory]
    [InlineData(29)]
    [InlineData(366)]
    public void Out_of_range_guest_inactive_days_fall_back_to_default_and_are_rejected(int days)
    {
        var options = new CheckOptions { GuestPendingInviteDays = 5, GuestInactiveDays = days };

        var normalized = CheckOptions.Normalize(options);

        Assert.Equal(90, normalized.GuestInactiveDays);
        Assert.Equal(5, normalized.GuestPendingInviteDays);
        Assert.Equal("Inactive guest days must be between 30 and 365.", CheckOptions.Validate(options));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3650)]
    public void Valid_guest_pending_invite_days_are_kept_and_accepted(int days)
    {
        var options = new CheckOptions { GuestPendingInviteDays = days };

        Assert.Equal(days, CheckOptions.Normalize(options).GuestPendingInviteDays);
        Assert.Null(CheckOptions.Validate(options));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Guest_pending_invite_days_below_one_fall_back_to_default_and_are_rejected(int days)
    {
        var options = new CheckOptions { GuestInactiveDays = 60, GuestPendingInviteDays = days };

        var normalized = CheckOptions.Normalize(options);

        Assert.Equal(30, normalized.GuestPendingInviteDays);
        Assert.Equal(60, normalized.GuestInactiveDays);
        Assert.Equal("Pending invitation days must be at least 1.", CheckOptions.Validate(options));
    }

    [Theory]
    [InlineData(30)]
    [InlineData(365)]
    public void Valid_user_inactive_days_are_kept_and_accepted(int days)
    {
        var options = new CheckOptions { UserInactiveDays = days };

        Assert.Equal(days, CheckOptions.Normalize(options).UserInactiveDays);
        Assert.Null(CheckOptions.Validate(options));
    }

    [Theory]
    [InlineData(29)]
    [InlineData(366)]
    public void Out_of_range_user_inactive_days_fall_back_to_default_and_are_rejected(int days)
    {
        var options = new CheckOptions { GuestInactiveDays = 60, UserInactiveDays = days };

        var normalized = CheckOptions.Normalize(options);

        Assert.Equal(90, normalized.UserInactiveDays);
        Assert.Equal(60, normalized.GuestInactiveDays);
        Assert.Equal("Inactive user days must be between 30 and 365.", CheckOptions.Validate(options));
    }
}
