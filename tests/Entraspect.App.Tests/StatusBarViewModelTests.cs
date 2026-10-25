using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Entraspect.App.Services;
using Entraspect.App.ViewModels;
using Entraspect.Core;

namespace Entraspect.App.Tests;

public sealed class StatusBarViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly SettingsState _settings =
        new(new SettingsStore(Path.Combine(Path.GetTempPath(), $"entraspect-status-{Guid.NewGuid()}")));

    [Fact]
    public void Not_connected_shows_only_that()
    {
        var bar = CreateBar();

        Assert.Equal(StatusBarViewModel.NotConnectedText, bar.ActivityText);
        Assert.False(bar.IsProgressVisible);
        Assert.False(bar.IsScanning);
        Assert.Equal(string.Empty, bar.ContextText);
        Assert.False(bar.HasContext);
        Assert.Equal(string.Empty, bar.ProfileName);
        Assert.False(bar.HasProfile);
    }

    [Fact]
    public void Connecting_shows_the_profile_with_an_indeterminate_bar()
    {
        var bar = CreateBar();

        bar.SetConnecting("Contoso");

        Assert.Equal("Connecting to Contoso…", bar.ActivityText);
        Assert.True(bar.IsProgressVisible);
        Assert.True(bar.IsProgressIndeterminate);
        Assert.False(bar.IsScanning);
    }

    [Fact]
    public void A_failed_connect_goes_back_to_not_connected()
    {
        var bar = CreateBar();
        bar.SetConnecting("Contoso");

        bar.SetConnecting(null);

        Assert.Equal(StatusBarViewModel.NotConnectedText, bar.ActivityText);
        Assert.False(bar.IsProgressVisible);
    }

    [Fact]
    public void Connected_with_no_scan_says_to_scan_and_shows_the_profile_name()
    {
        var bar = CreateBar();

        bar.SetConnection("Contoso");

        Assert.Equal(StatusBarViewModel.ConnectedText, bar.ActivityText);
        Assert.False(bar.IsProgressVisible);
        Assert.Equal("Contoso", bar.ProfileName);
        Assert.True(bar.HasProfile);
    }

    [Fact]
    public void A_starting_scan_is_indeterminate_and_can_be_cancelled()
    {
        var bar = CreateBar();
        bar.SetConnection("Contoso");

        bar.ScanStarted();

        Assert.Equal(StatusBarViewModel.StartingScanText, bar.ActivityText);
        Assert.True(bar.IsProgressVisible);
        Assert.True(bar.IsProgressIndeterminate);
        Assert.True(bar.IsScanning);
    }

    [Fact]
    public void A_running_scan_shows_the_check_and_fills_as_checks_finish()
    {
        var bar = CreateBar();
        bar.SetConnection("Contoso");
        bar.ScanStarted();

        bar.ScanProgressed(new ScanProgress(1, 4, "Expiring credentials"));

        Assert.Equal("Running check 1 of 4: Expiring credentials", bar.ActivityText);
        Assert.False(bar.IsProgressIndeterminate);
        Assert.Equal(0, bar.ProgressValue);
        Assert.True(bar.IsScanning);

        bar.ScanProgressed(new ScanProgress(3, 4, "Policy hygiene"));
        Assert.Equal(0.5, bar.ProgressValue);

        // A check's own detail doesn't move the bar.
        bar.ScanProgressed(new ScanProgress(3, 4, "Policy hygiene", "looking up user 4 of 20"));
        Assert.Equal("Running check 3 of 4: Policy hygiene (looking up user 4 of 20)", bar.ActivityText);
        Assert.Equal(0.5, bar.ProgressValue);
    }

    [Theory]
    [InlineData(ScanEnd.Finished, "Scan finished at 12:00 UTC")]
    [InlineData(ScanEnd.Cancelled, StatusBarViewModel.CancelledText)]
    [InlineData(ScanEnd.Failed, StatusBarViewModel.FailedText)]
    public void After_a_scan_shows_how_it_ended_and_cancel_is_gone(ScanEnd end, string text)
    {
        var bar = CreateBar();
        bar.SetConnection("Contoso");
        bar.ScanStarted();
        bar.ScanProgressed(new ScanProgress(2, 4, "Expiring credentials"));

        bar.ScanEnded(end, Now);

        Assert.Equal(text, bar.ActivityText);
        Assert.False(bar.IsProgressVisible);
        Assert.False(bar.IsScanning);
        Assert.Equal("Contoso", bar.ProfileName);
    }

    [Fact]
    public void An_export_is_shown_until_the_next_scan()
    {
        var bar = CreateBar();
        bar.SetConnection("Contoso");
        bar.ScanStarted();
        bar.ScanEnded(ScanEnd.Finished, Now);

        bar.Exported("scan.csv");
        Assert.Equal("Exported to scan.csv", bar.ActivityText);

        bar.ScanStarted();
        Assert.Equal(StatusBarViewModel.StartingScanText, bar.ActivityText);
    }

    [Fact]
    public void Disconnecting_clears_the_scan_and_the_profile_name()
    {
        var bar = CreateBar();
        bar.SetConnection("Contoso");
        bar.ScanStarted();
        bar.ScanEnded(ScanEnd.Finished, Now);

        bar.SetConnection(null);

        Assert.Equal(StatusBarViewModel.NotConnectedText, bar.ActivityText);
        Assert.False(bar.HasProfile);
    }

    [Fact]
    public void A_new_connection_forgets_the_last_scan()
    {
        var bar = CreateBar();
        bar.SetConnection("Contoso");
        bar.ScanStarted();
        bar.ScanEnded(ScanEnd.Cancelled);

        bar.SetConnection("Fabrikam");

        Assert.Equal(StatusBarViewModel.ConnectedText, bar.ActivityText);
        Assert.Equal("Fabrikam", bar.ProfileName);
    }

    [Fact]
    public void A_check_page_shows_how_its_check_last_ran()
    {
        var bar = CreateBar();
        var page = CreatePage();
        bar.SetConnection("Contoso");
        bar.SetPage(page);
        Assert.Equal(string.Empty, bar.ContextText);
        Assert.False(bar.HasContext);

        page.SetResult(Result(CheckResultStatus.Succeeded));
        Assert.Equal("Last run 2026-01-15 12:00 UTC", bar.ContextText);
        Assert.True(bar.HasContext);

        page.SetResult(Result(CheckResultStatus.Succeeded, dataUpdatedAt: Now.AddHours(-6)));
        Assert.Equal("Last run 2026-01-15 12:00 UTC · Report updated 2026-01-15 06:00 UTC", bar.ContextText);

        page.SetResult(Result(CheckResultStatus.Failed));
        Assert.Equal("Last run 2026-01-15 12:00 UTC failed", bar.ContextText);

        page.SetResult(Result(CheckResultStatus.MissingPermissions));
        Assert.Equal("Not run: missing permissions", bar.ContextText);

        page.SetResult(Result(CheckResultStatus.Cancelled));
        Assert.Equal("Last scan cancelled", bar.ContextText);

        page.SetResult(null);
        Assert.Equal(string.Empty, bar.ContextText);
    }

    [Fact]
    public void Leaving_a_check_page_stops_following_it()
    {
        var bar = CreateBar();
        var page = CreatePage();
        bar.SetPage(page);

        bar.SetPage(null);
        page.SetResult(Result(CheckResultStatus.Succeeded));

        Assert.Equal(string.Empty, bar.ContextText);
    }

    [Fact]
    public void Overview_shows_the_last_scan()
    {
        var bar = CreateBar();
        bar.SetConnection("Contoso");
        bar.SetPage(CreateOverview());
        Assert.Equal(string.Empty, bar.ContextText);

        bar.SetLastScan(Now);

        Assert.Equal("Last scan 2026-01-15 12:00 UTC", bar.ContextText);
    }

    [Fact]
    public void A_settings_page_shows_only_the_profile_name()
    {
        var bar = CreateBar();
        bar.SetConnection("Contoso");
        bar.SetLastScan(Now);

        bar.SetPage(new PreferencesViewModel(_settings, new TimeDisplay(_settings), new FakeUserInteraction()));

        Assert.Equal(string.Empty, bar.ContextText);
        Assert.Equal("Contoso", bar.ProfileName);
    }

    [Fact]
    public void Times_follow_the_time_zone_preference()
    {
        var bar = CreateBar();
        bar.SetConnection("Contoso");
        bar.SetPage(CreateOverview());
        bar.ScanStarted();
        bar.ScanEnded(ScanEnd.Finished, Now);
        bar.SetLastScan(Now);

        _settings.SetTimeZone(AppSettings.LocalTimeZone);
        bar.Refresh();

        Assert.Equal("Scan finished at 14:00", bar.ActivityText);
        Assert.Equal("Last scan 2026-01-15 14:00", bar.ContextText);
    }

    [Fact]
    public void Cancel_is_the_given_command()
    {
        var cancel = new RelayCommand(() => { });

        var bar = new StatusBarViewModel(new TimeDisplay(_settings, TestTimeZones.Create()), cancel);

        Assert.Same(cancel, bar.CancelCommand);
    }

    private StatusBarViewModel CreateBar() =>
        new(new TimeDisplay(_settings, TestTimeZones.Create()), new RelayCommand(() => { }));

    private CheckPageViewModel CreatePage() => new(
        CheckCatalog.All(TimeProvider.System).First(),
        new PermissionBannerViewModel(() => Task.CompletedTask, _ => Task.CompletedTask, new RelayCommand(() => { })),
        new TimeDisplay(_settings, TestTimeZones.Create()));

    private static OverviewViewModel CreateOverview() => new(
        new PermissionBannerViewModel(() => Task.CompletedTask, _ => Task.CompletedTask, new RelayCommand(() => { })),
        new ExcessPermissionsViewModel(),
        _ => { });

    private static CheckResult Result(CheckResultStatus status, DateTimeOffset? dataUpdatedAt = null) => new()
    {
        CheckId = "fake",
        Status = status,
        CompletedAt = status is CheckResultStatus.Succeeded or CheckResultStatus.Failed ? Now : null,
        DataUpdatedAt = dataUpdatedAt,
    };
}
