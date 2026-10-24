using System.IO;
using Entraspect.App.Services;
using Entraspect.App.ViewModels;
using Entraspect.Core;

namespace Entraspect.App.Tests;

public sealed class ChecksSettingsViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"entraspect-checks-{Guid.NewGuid()}");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void Turning_a_check_off_is_saved_and_survives_a_restart()
    {
        var vm = Create();

        Assert.Single(vm.Rows).IsEnabled = false;

        var reloaded = Create();
        Assert.False(Assert.Single(reloaded.Rows).IsEnabled);
        Assert.Equal(new[] { "app-credential-expiry" }, new SettingsStore(_dir).Load(out _).DisabledCheckIds);
    }

    [Fact]
    public void Valid_options_are_saved_immediately()
    {
        var vm = Create();

        vm.ExpiryWarningDays = 45;
        vm.ExpiryCriticalDays = 10;

        Assert.Equal(string.Empty, vm.OptionsErrorText);
        var options = new SettingsStore(_dir).Load(out _).CheckOptions;
        Assert.Equal((45, 10), (options.ExpiryWarningDays, options.ExpiryCriticalDays));
    }

    [Fact]
    public void Invalid_options_show_an_error_and_are_not_saved()
    {
        var vm = Create();
        vm.ExpiryWarningDays = 20;

        vm.ExpiryCriticalDays = 25;

        Assert.Contains("Not saved", vm.OptionsErrorText);
        Assert.Equal(7, new SettingsStore(_dir).Load(out _).CheckOptions.ExpiryCriticalDays);
    }

    [Fact]
    public void Permission_status_is_shown_only_when_connected()
    {
        var vm = Create();
        var row = Assert.Single(vm.Rows);
        var permission = Assert.Single(row.Permissions);
        Assert.False(permission.IsGranted || permission.IsMissing);

        vm.SetGrantedPermissions(Array.Empty<string>());
        Assert.True(Assert.Single(row.Permissions).IsMissing);

        vm.SetGrantedPermissions(new[] { "Application.Read.All" });
        Assert.True(Assert.Single(row.Permissions).IsGranted);
    }

    private ChecksSettingsViewModel Create() => new(
        CheckCatalog.All(TimeProvider.System),
        MainViewModel.CategoryTitle,
        new SettingsState(new SettingsStore(_dir)));
}
