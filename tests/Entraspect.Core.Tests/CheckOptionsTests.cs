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
}
