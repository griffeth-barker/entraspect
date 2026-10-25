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

        AppCheckRow(vm).IsEnabled = false;

        var reloaded = Create();
        Assert.False(AppCheckRow(reloaded).IsEnabled);
        Assert.All(reloaded.Rows.Where(r => r.Check.Id != AppCheckId), r => Assert.True(r.IsEnabled));
        Assert.Equal(new[] { "app-credential-expiry" }, new SettingsStore(_dir).Load(out _).DisabledCheckIds);
    }

    [Fact]
    public void Valid_options_are_saved_immediately()
    {
        var vm = Create();

        vm.ExpiryWarningDays = 45;
        vm.ExpiryCriticalDays = 10;
        vm.NewGroupGraceDays = 0;

        Assert.Equal(string.Empty, vm.OptionsErrorText);
        var options = new SettingsStore(_dir).Load(out _).CheckOptions;
        Assert.Equal((45, 10, 0), (options.ExpiryWarningDays, options.ExpiryCriticalDays, options.NewGroupGraceDays));
        Assert.Equal(0, Create().NewGroupGraceDays);
    }

    [Fact]
    public void Negative_new_group_grace_days_show_an_error_and_are_not_saved()
    {
        var vm = Create();

        vm.NewGroupGraceDays = -1;

        Assert.Contains("Not saved", vm.OptionsErrorText);
        Assert.Equal(7, new SettingsStore(_dir).Load(out _).CheckOptions.NewGroupGraceDays);
    }

    [Fact]
    public void Group_checks_are_listed_with_their_permission()
    {
        var vm = Create();

        var groupRows = vm.Rows.Where(r => r.CategoryTitle == "Groups").ToList();

        Assert.Equal(new[] { "Groups with no members", "Groups with no owners" }, groupRows.Select(r => r.Title));
        Assert.Equal(new[] { "GroupMember.Read.All", "Member.Read.Hidden" }, groupRows[0].Permissions.Select(p => p.Name));
        Assert.Equal(new[] { "GroupMember.Read.All" }, groupRows[1].Permissions.Select(p => p.Name));
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
        var row = AppCheckRow(vm);
        var permission = Assert.Single(row.Permissions);
        Assert.False(permission.IsGranted || permission.IsMissing);

        vm.SetGrantedPermissions(Array.Empty<string>());
        Assert.True(Assert.Single(row.Permissions).IsMissing);

        vm.SetGrantedPermissions(new[] { "Application.Read.All" });
        Assert.True(Assert.Single(row.Permissions).IsGranted);
    }

    private const string AppCheckId = "app-credential-expiry";

    private static CheckSettingsRow AppCheckRow(ChecksSettingsViewModel vm) => vm.Rows.Single(r => r.Check.Id == AppCheckId);

    private ChecksSettingsViewModel Create() => new(
        CheckCatalog.All(TimeProvider.System),
        MainViewModel.CategoryTitle,
        new SettingsState(new SettingsStore(_dir)));
}
