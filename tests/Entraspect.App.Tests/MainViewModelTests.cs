using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Entraspect.App.Services;
using Entraspect.App.ViewModels;
using Entraspect.Core;
using Entraspect.Graph;
using Microsoft.Extensions.Time.Testing;

namespace Entraspect.App.Tests;

public sealed class MainViewModelTests : IDisposable
{
    private const string CheckId = "app-credential-expiry";
    private const string FakePassword = "fake-pfx-password-for-test";

    private static readonly DateTimeOffset Now = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"entraspect-main-{Guid.NewGuid()}");
    private readonly FakeTimeProvider _time = new(Now);
    private readonly FakePasswordPrompt _prompt = new();
    private readonly List<(ConnectionProfile Profile, string? Password)> _connectCalls = [];

    private IDirectoryReader _reader = new FakeReader(OneExpiringApp());
    private IReadOnlyList<string> _granted = ["Application.Read.All"];
    private Exception? _connectError;

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void Starts_on_connection_profiles_with_overview_and_checks_disabled()
    {
        var vm = CreateViewModel();

        Assert.Same(vm.ConnectionProfilesItem, vm.SelectedNavItem);
        Assert.Same(vm.Profiles, vm.CurrentPage);
        Assert.True(vm.SettingsItem.IsExpanded);
        Assert.False(vm.IsConnected);
        Assert.Equal("Not connected", vm.ConnectionStatusText);
        Assert.False(vm.ScanCommand.CanExecute(null));

        Assert.False(vm.OverviewItem.IsEnabled);
        Assert.Equal(NavItemViewModel.ConnectFirstToolTip, vm.OverviewItem.ToolTip);
        var checkItem = CheckItem(vm);
        Assert.False(checkItem.IsEnabled);
        Assert.Equal(NavItemViewModel.ConnectFirstToolTip, checkItem.ToolTip);
        Assert.All(vm.SettingsItem.Children, item => Assert.True(item.IsEnabled));
    }

    [Fact]
    public void Nav_has_overview_then_one_section_per_category_with_checks()
    {
        var vm = CreateViewModel();

        Assert.Same(vm.OverviewItem, vm.MenuItems[0]);
        var section = Assert.Single(vm.MenuItems.Skip(1));
        Assert.Equal("App registrations", section.Title);
        Assert.False(section.IsPage);
        Assert.False(section.IsExpanded);
        Assert.Equal(CheckId, Assert.IsType<CheckPageViewModel>(Assert.Single(section.Children).Page).Check.Id);
        Assert.Equal(
            new[] { "Connection profiles", "Checks", "About" },
            Assert.Single(vm.FooterItems).Children.Select(c => c.Title));
    }

    [Fact]
    public void Selecting_a_disabled_page_or_a_section_keeps_the_current_page()
    {
        var vm = CreateViewModel();

        vm.SelectedNavItem = vm.OverviewItem;
        Assert.Same(vm.ConnectionProfilesItem, vm.SelectedNavItem);

        vm.SelectedNavItem = vm.SettingsItem;
        Assert.Same(vm.ConnectionProfilesItem, vm.SelectedNavItem);
        Assert.Same(vm.Profiles, vm.CurrentPage);
    }

    [Fact]
    public async Task Connect_with_a_thumbprint_opens_overview_without_asking_for_a_password()
    {
        SaveProfiles(new ConnectionProfile { Name = "p1", TenantId = "t1", ClientId = "c1", CertificateThumbprint = "AB" });
        var vm = CreateViewModel(lastUsed: "p1");

        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.Equal(0, _prompt.Calls);
        Assert.Null(Assert.Single(_connectCalls).Password);
        Assert.True(vm.IsConnected);
        Assert.Equal("Connected", vm.ConnectionStatusText);
        Assert.Equal("p1", vm.ConnectedProfileName);
        Assert.Equal("t1", vm.ConnectedTenantId);
        Assert.Same(vm.OverviewItem, vm.SelectedNavItem);
        Assert.Same(vm.Overview, vm.CurrentPage);
        Assert.True(vm.OverviewItem.IsEnabled);
        Assert.True(CheckItem(vm).IsEnabled);
        Assert.Null(CheckItem(vm).ToolTip);
        Assert.True(vm.ScanCommand.CanExecute(null));
    }

    [Fact]
    public async Task Connect_with_a_pfx_asks_for_the_password_and_never_saves_it()
    {
        SaveProfiles(new ConnectionProfile { Name = "p1", TenantId = "t1", ClientId = "c1", CertificatePath = "/x.pfx" });
        _prompt.Password = FakePassword;
        var vm = CreateViewModel(lastUsed: "p1");

        await vm.ConnectCommand.ExecuteAsync(null);
        vm.Profiles.SaveProfileCommand.Execute(null);

        Assert.Equal(1, _prompt.Calls);
        Assert.Equal(FakePassword, Assert.Single(_connectCalls).Password);
        Assert.True(vm.IsConnected);
        foreach (var file in Directory.GetFiles(_dir))
        {
            Assert.DoesNotContain(FakePassword, File.ReadAllText(file));
        }
    }

    [Fact]
    public async Task Cancelling_the_password_dialog_does_not_connect()
    {
        SaveProfiles(new ConnectionProfile { Name = "p1", TenantId = "t1", ClientId = "c1", CertificatePath = "/x.pfx" });
        _prompt.Password = null;
        var vm = CreateViewModel(lastUsed: "p1");

        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.Empty(_connectCalls);
        Assert.False(vm.IsConnected);
        Assert.Same(vm.ConnectionProfilesItem, vm.SelectedNavItem);
    }

    [Fact]
    public async Task Connect_failure_shows_the_error_and_stays_disconnected()
    {
        _connectError = new DirectoryReadException("Sign-in failed: AADSTS700016 fake test error");
        var vm = CreateConnectableViewModel();

        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.False(vm.IsConnected);
        Assert.Contains("AADSTS700016", vm.ErrorText);
        Assert.False(vm.OverviewItem.IsEnabled);
    }

    [Fact]
    public async Task Connect_saves_the_last_used_profile()
    {
        var vm = CreateConnectableViewModel();

        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.Equal("p1", new SettingsStore(_dir).Load(out _).LastUsedProfileName);
    }

    [Fact]
    public async Task Scan_fills_the_badge_and_the_check_page()
    {
        var vm = CreateConnectableViewModel();
        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.False(CheckItem(vm).HasBadge);
        await vm.ScanCommand.ExecuteAsync(null);

        var item = CheckItem(vm);
        Assert.True(item.HasBadge);
        Assert.False(item.HasWarning);
        Assert.Equal(1, item.BadgeValue);
        Assert.Single(CheckPage(vm).Findings);
        Assert.False(vm.IsScanning);
        Assert.StartsWith("Scan finished", vm.ProgressText);
        Assert.StartsWith("Last scan:", vm.Overview.LastScanText);
    }

    [Fact]
    public async Task A_check_missing_permissions_shows_a_warning_marker()
    {
        _granted = [];
        var vm = CreateConnectableViewModel();
        await vm.ConnectCommand.ExecuteAsync(null);

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.True(CheckItem(vm).HasBadge);
        Assert.True(CheckItem(vm).HasWarning);
        Assert.Contains("Application.Read.All", CheckPage(vm).ErrorText);
    }

    [Fact]
    public async Task A_failed_check_shows_a_warning_marker()
    {
        _reader = new FailingReader();
        var vm = CreateConnectableViewModel();
        await vm.ConnectCommand.ExecuteAsync(null);

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.True(CheckItem(vm).HasWarning);
        Assert.Contains("fake read failure", CheckPage(vm).ErrorText);
    }

    [Fact]
    public async Task A_disabled_check_is_not_run_and_has_no_badge()
    {
        new SettingsStore(_dir).Save(new AppSettings { DisabledCheckIds = [CheckId], LastUsedProfileName = "p1" });
        var vm = CreateConnectableViewModel();
        await vm.ConnectCommand.ExecuteAsync(null);

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.False(CheckItem(vm).HasBadge);
        Assert.Equal(0, ((FakeReader)_reader).Reads);
    }

    [Fact]
    public async Task Cancel_stops_a_running_scan()
    {
        var reader = new BlockingReader();
        _reader = reader;
        var vm = CreateConnectableViewModel();
        await vm.ConnectCommand.ExecuteAsync(null);

        var scan = vm.ScanCommand.ExecuteAsync(null);
        await reader.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(vm.CancelCommand.CanExecute(null));
        vm.CancelCommand.Execute(null);
        await scan.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(vm.IsScanning);
        Assert.Equal("Scan cancelled.", vm.ProgressText);
        Assert.False(CheckItem(vm).HasBadge);
    }

    [Fact]
    public async Task Disconnect_clears_results_and_returns_to_connection_profiles()
    {
        var vm = CreateConnectableViewModel();
        await vm.ConnectCommand.ExecuteAsync(null);
        await vm.ScanCommand.ExecuteAsync(null);

        vm.DisconnectCommand.Execute(null);

        Assert.False(vm.IsConnected);
        Assert.Same(vm.ConnectionProfilesItem, vm.SelectedNavItem);
        Assert.False(vm.OverviewItem.IsEnabled);
        Assert.False(CheckItem(vm).IsEnabled);
        Assert.False(CheckItem(vm).HasBadge);
        Assert.Empty(CheckPage(vm).Findings);
        Assert.Equal(OverviewViewModel.NoScanText, vm.Overview.LastScanText);
        Assert.False(vm.ScanCommand.CanExecute(null));
    }

    [Fact]
    public async Task Reconnect_gets_new_permissions_and_clears_old_results()
    {
        SaveProfiles(new ConnectionProfile { Name = "p1", TenantId = "t1", ClientId = "c1", CertificatePath = "/x.pfx" });
        _prompt.Password = FakePassword;
        var vm = CreateViewModel(lastUsed: "p1");
        await vm.ConnectCommand.ExecuteAsync(null);
        await vm.ScanCommand.ExecuteAsync(null);

        _granted = ["Application.Read.All", "Directory.Read.All"];
        await vm.ReconnectCommand.ExecuteAsync(null);

        Assert.Equal(2, _connectCalls.Count);
        Assert.Equal(2, _prompt.Calls);
        Assert.Contains("Directory.Read.All", vm.GrantedPermissions);
        Assert.False(CheckItem(vm).HasBadge);
    }

    private MainViewModel CreateConnectableViewModel()
    {
        SaveProfiles(new ConnectionProfile { Name = "p1", TenantId = "t1", ClientId = "c1", CertificateThumbprint = "AB" });
        return CreateViewModel(lastUsed: "p1");
    }

    private MainViewModel CreateViewModel(string? lastUsed = null)
    {
        if (lastUsed is not null && !File.Exists(Path.Combine(_dir, "settings.json")))
        {
            new SettingsStore(_dir).Save(new AppSettings { LastUsedProfileName = lastUsed });
        }

        return new MainViewModel(new ProfileStore(_dir), new SettingsStore(_dir), Connect, _prompt, _time);
    }

    private Task<DirectoryConnection> Connect(ConnectionProfile profile, string? password, CancellationToken ct)
    {
        _connectCalls.Add((profile, password));
        if (_connectError is not null)
        {
            throw _connectError;
        }

        return Task.FromResult(new DirectoryConnection
        {
            Reader = _reader,
            ClientId = profile.ClientId,
            GrantedPermissions = _granted,
        });
    }

    private void SaveProfiles(params ConnectionProfile[] profiles) => new ProfileStore(_dir).Save(profiles);

    private static NavItemViewModel CheckItem(MainViewModel vm) =>
        vm.MenuItems.SelectMany(i => i.Children).Single(i => i.Kind == NavItemKind.Check);

    private static CheckPageViewModel CheckPage(MainViewModel vm) => (CheckPageViewModel)CheckItem(vm).Page!;

    private static IReadOnlyList<AppRegistration> OneExpiringApp() =>
    [
        new AppRegistration
        {
            Id = "obj-1",
            AppId = Guid.NewGuid(),
            DisplayName = "Fake app",
            Credentials =
            [
                new AppCredential
                {
                    KeyId = Guid.NewGuid(),
                    Type = AppCredentialType.Secret,
                    DisplayName = "fake secret",
                    StartDateTime = Now.AddDays(-300),
                    EndDateTime = Now.AddDays(3),
                },
            ],
        },
    ];

    private sealed class FakePasswordPrompt : IPasswordPrompt
    {
        public string? Password { get; set; }

        public int Calls { get; private set; }

        public Task<string?> RequestPasswordAsync(string profileName)
        {
            Calls++;
            return Task.FromResult(Password);
        }
    }

    private sealed class FakeReader(IReadOnlyList<AppRegistration> apps) : IDirectoryReader
    {
        public int Reads { get; private set; }

        public Task<IReadOnlyList<AppRegistration>> GetAppRegistrationsAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult(apps);
        }
    }

    private sealed class FailingReader : IDirectoryReader
    {
        public Task<IReadOnlyList<AppRegistration>> GetAppRegistrationsAsync(CancellationToken cancellationToken = default) =>
            throw new DirectoryReadException("fake read failure", 500);
    }

    private sealed class BlockingReader : IDirectoryReader
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IReadOnlyList<AppRegistration>> GetAppRegistrationsAsync(CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return [];
        }
    }
}
