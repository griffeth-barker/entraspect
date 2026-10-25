using System.IO;
using Entraspect.App.Services;
using Entraspect.App.ViewModels;

namespace Entraspect.App.Tests;

public sealed class PreferencesViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"entraspect-prefs-{Guid.NewGuid()}");

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

    private PreferencesViewModel Create(out SettingsState settings)
    {
        settings = new SettingsState(new SettingsStore(_dir));
        return new PreferencesViewModel(settings, new TimeDisplay(settings, TestTimeZones.Create()));
    }
}
