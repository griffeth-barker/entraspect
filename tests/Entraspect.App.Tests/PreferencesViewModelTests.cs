using System.IO;
using Entraspect.App.Services;
using Entraspect.App.ViewModels;

namespace Entraspect.App.Tests;

public sealed class PreferencesViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"entraspect-prefs-{Guid.NewGuid()}");
    private readonly FakeUserInteraction _ui = new();

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void Time_zones_list_utc_then_local_then_every_zone()
    {
        var vm = Create(out _);

        Assert.Equal(
            ["UTC", "Local", "America/New_York", "Europe/Berlin", "Asia/Tokyo"],
            vm.TimeZoneChoices.Select(c => c.Id));
        Assert.Equal("UTC", vm.TimeZoneChoices[0].Name);
        Assert.Equal("Local time (Test+2)", vm.TimeZoneChoices[1].Name);
        Assert.Equal("(UTC+09:00) Osaka, Sapporo, Tokyo", vm.TimeZoneChoices[4].Name);
        Assert.Equal("UTC", vm.SelectedTimeZone?.Id);
        Assert.Equal(string.Empty, vm.TimeZoneNotice);
    }

    [Fact]
    public void Choosing_a_zone_saves_it()
    {
        var vm = Create(out var settings);

        vm.SelectedTimeZone = vm.TimeZoneChoices.Single(c => c.Id == "Asia/Tokyo");

        Assert.Equal("Asia/Tokyo", settings.Current.TimeZone);
        Assert.Equal("Asia/Tokyo", new SettingsStore(_dir).Load(out _).TimeZone);
        Assert.Equal(string.Empty, vm.ErrorText);
    }

    [Fact]
    public void An_unknown_saved_zone_selects_utc_with_a_notice_and_is_not_overwritten()
    {
        new SettingsStore(_dir).Save(new AppSettings { TimeZone = "Mars/Olympus_Mons" });

        var vm = Create(out var settings);

        Assert.Equal("UTC", vm.SelectedTimeZone?.Id);
        Assert.Equal(PreferencesViewModel.ZoneUnavailableText, vm.TimeZoneNotice);
        Assert.Equal("Mars/Olympus_Mons", new SettingsStore(_dir).Load(out _).TimeZone);

        vm.SelectedTimeZone = vm.TimeZoneChoices.Single(c => c.Id == "Europe/Berlin");

        Assert.Equal(string.Empty, vm.TimeZoneNotice);
        Assert.Equal("Europe/Berlin", settings.Current.TimeZone);
    }

    [Fact]
    public void A_windows_id_selects_the_matching_zone()
    {
        new SettingsStore(_dir).Save(new AppSettings { TimeZone = "Tokyo Standard Time" });

        var vm = Create(out _);

        Assert.Equal("Asia/Tokyo", vm.SelectedTimeZone?.Id);
        Assert.Equal(string.Empty, vm.TimeZoneNotice);
    }

    [Fact]
    public void The_saved_theme_is_applied_when_created()
    {
        new SettingsStore(_dir).Save(new AppSettings { Theme = AppTheme.Dark });

        var vm = Create(out _);

        Assert.Equal(AppTheme.Dark, vm.SelectedTheme?.Theme);
        Assert.Equal([AppTheme.Dark], _ui.Themes);
    }

    [Fact]
    public void Use_system_setting_is_the_default_theme()
    {
        var vm = Create(out _);

        Assert.Equal(["Use system setting", "Light", "Dark"], vm.ThemeChoices.Select(c => c.Name));
        Assert.Equal(AppTheme.System, vm.SelectedTheme?.Theme);
        Assert.Equal([AppTheme.System], _ui.Themes);
    }

    [Fact]
    public void Choosing_a_theme_saves_and_applies_it()
    {
        var vm = Create(out var settings);

        vm.SelectedTheme = vm.ThemeChoices.Single(c => c.Theme == AppTheme.Light);

        Assert.Equal(AppTheme.Light, settings.Current.Theme);
        Assert.Equal(AppTheme.Light, new SettingsStore(_dir).Load(out _).Theme);
        Assert.Equal([AppTheme.System, AppTheme.Light], _ui.Themes);
    }

    [Fact]
    public void Other_settings_changes_do_not_reapply_the_theme()
    {
        var vm = Create(out var settings);

        settings.SetTimeZone("Asia/Tokyo");

        Assert.Equal([AppTheme.System], _ui.Themes);
        Assert.Equal("Asia/Tokyo", vm.SelectedTimeZone?.Id);
    }

    private PreferencesViewModel Create(out SettingsState settings)
    {
        settings = new SettingsState(new SettingsStore(_dir));
        return new PreferencesViewModel(settings, new TimeDisplay(settings, TestTimeZones.Create()), _ui);
    }
}
