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

        vm.SetConnection(Array.Empty<string>(), TenantLicence.Unknown);
        Assert.True(Assert.Single(row.Permissions).IsMissing);

        vm.SetConnection(new[] { "Application.Read.All" }, TenantLicence.Unknown);
        Assert.True(Assert.Single(row.Permissions).IsGranted);
    }

    [Fact]
    public void Unlicensed_check_grays_out_its_toggle_and_keeps_its_saved_choice()
    {
        var vm = Create();
        var row = MfaRow(vm);
        Assert.True(row.IsLicensed);
        Assert.True(row.IsEnabled);

        vm.SetConnection(new[] { "AuditLog.Read.All" }, TenantLicence.Free);

        Assert.False(row.IsLicensed);
        Assert.False(row.IsOn);
        Assert.True(row.IsEnabled);
        row.IsOn = true;
        Assert.False(row.IsOn);
        Assert.Equal("Needs an Entra ID P1 or P2 licence.", row.LicenceNote);
        Assert.Empty(new SettingsStore(_dir).Load(out _).DisabledCheckIds);
        Assert.True(AppCheckRow(vm).IsLicensed);

        vm.SetConnection(new[] { "AuditLog.Read.All" }, TenantLicence.EntraIdP1);
        Assert.True(row.IsLicensed);
        Assert.True(row.IsOn);

        row.IsOn = false;
        Assert.False(row.IsEnabled);
        Assert.Equal(new[] { "users-mfa-not-registered" }, new SettingsStore(_dir).Load(out _).DisabledCheckIds);

        vm.SetConnection(new[] { "AuditLog.Read.All" }, TenantLicence.Unknown);
        Assert.True(row.IsLicensed);
        Assert.False(row.IsOn);
    }

    [Fact]
    public void Licence_permission_label_shows_only_on_licence_gated_checks_and_is_never_red()
    {
        var vm = Create();
        Assert.Null(AppCheckRow(vm).LicencePermission);

        var label = MfaRow(vm).LicencePermission!;
        Assert.Equal("LicenseAssignment.Read.All", label.Name);
        Assert.False(label.IsGranted || label.IsMissing);

        vm.SetConnection(Array.Empty<string>(), TenantLicence.Unknown);
        label = MfaRow(vm).LicencePermission!;
        Assert.False(label.IsGranted);
        Assert.False(label.IsMissing);
        Assert.Equal(PermissionStatusViewModel.LicenceToolTip, label.ToolTipText);

        vm.SetConnection(new[] { "LicenseAssignment.Read.All" }, TenantLicence.Free);
        Assert.True(MfaRow(vm).LicencePermission!.IsGranted);
    }

    private static CheckSettingsRow MfaRow(ChecksSettingsViewModel vm) =>
        vm.Rows.Single(r => r.Check.Id == "users-mfa-not-registered");

    private const string AppCheckId = "app-credential-expiry";

    private static CheckSettingsRow AppCheckRow(ChecksSettingsViewModel vm) => vm.Rows.Single(r => r.Check.Id == AppCheckId);

    private ChecksSettingsViewModel Create() => new(
        CheckCatalog.All(TimeProvider.System),
        MainViewModel.CategoryTitle,
        new SettingsState(new SettingsStore(_dir)));
}
