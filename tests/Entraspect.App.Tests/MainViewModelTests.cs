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

    private const string SamlCheckId = "saml-signing-certificate-expiry";

    private const string MfaCheckId = "users-mfa-not-registered";
    private const string FakePassword = "fake-pfx-password-for-test";
    private const string ClientId = "00000000-0000-0000-0000-0000000000c1";

    private static readonly DateTimeOffset Now = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeZoneInfo PlusTwoHours =
        TimeZoneInfo.CreateCustomTimeZone("Test+2", TimeSpan.FromHours(2), "Test+2", "Test+2");

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"entraspect-main-{Guid.NewGuid()}");
    private readonly FakeTimeProvider _time = new(Now);
    private readonly FakeUserInteraction _prompt = new();
    private readonly List<(ConnectionProfile Profile, string? Password)> _connectCalls = [];

    private IDirectoryReader _reader = new FakeReader(OneExpiringApp());
    private IReadOnlyList<string> _granted =
        ["Application.Read.All", "GroupMember.Read.All", "Member.Read.Hidden", "AuditLog.Read.All"];
    private Exception? _connectError;
    private TenantLicence _licence = TenantLicence.Unknown;

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
        Assert.Equal(
            new[] { "Users", "Groups", "App registrations", "Enterprise apps" },
            vm.MenuItems.Skip(1).Select(i => i.Title));
        Assert.Equal(
            MfaCheckId,
            Assert.IsType<CheckPageViewModel>(Assert.Single(vm.MenuItems[1].Children).Page).Check.Id);
        Assert.Equal(
            new[] { "group-no-members", "group-no-owners" },
            vm.MenuItems[2].Children.Select(c => Assert.IsType<CheckPageViewModel>(c.Page).Check.Id));
        var section = vm.MenuItems[3];
        Assert.False(section.IsPage);
        Assert.False(section.IsExpanded);
        Assert.Equal(CheckId, Assert.IsType<CheckPageViewModel>(Assert.Single(section.Children).Page).Check.Id);
        Assert.Equal(
            SamlCheckId,
            Assert.IsType<CheckPageViewModel>(Assert.Single(vm.MenuItems[4].Children).Page).Check.Id);
        Assert.Equal(
            new[] { "Connection profiles", "Checks", "Preferences", "About" },
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
        SaveProfiles(new ConnectionProfile { Name = "p1", TenantId = "t1", ClientId = ClientId, CertificateThumbprint = "AB" });
        var vm = CreateViewModel(lastUsed: "p1");

        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.Equal(0, _prompt.Calls);
        Assert.Null(Assert.Single(_connectCalls).Password);
        Assert.True(vm.IsConnected);
        Assert.Equal("Connected", vm.ConnectionStatusText);
        Assert.True(vm.Overview.IsConnected);
        Assert.Equal("p1", vm.Overview.ProfileName);
        Assert.Equal("t1", vm.Overview.TenantId);
        Assert.Equal(ClientId, vm.Overview.ClientId);
        Assert.Equal("2026-01-15 12:00 UTC", vm.Overview.ConnectedAtText);
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
        SaveProfiles(new ConnectionProfile { Name = "p1", TenantId = "t1", ClientId = ClientId, CertificatePath = "/x.pfx" });
        _prompt.Password = FakePassword;
        var vm = CreateViewModel(lastUsed: "p1");

        await vm.ConnectCommand.ExecuteAsync(null);
        vm.Profiles.EditCommand.Execute(null);
        vm.Profiles.SaveCommand.Execute(null);

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
        SaveProfiles(new ConnectionProfile { Name = "p1", TenantId = "t1", ClientId = ClientId, CertificatePath = "/x.pfx" });
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
        Assert.Contains("AADSTS700016", vm.ConnectionErrorText);
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
        Assert.Equal(["Application.Read.All"], CheckPage(vm).Banner.MissingPermissions);
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
        Assert.False(vm.Overview.IsConnected);
        Assert.Equal(string.Empty, vm.Overview.TenantId);
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
        SaveProfiles(new ConnectionProfile { Name = "p1", TenantId = "t1", ClientId = ClientId, CertificatePath = "/x.pfx" });
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

    [Fact]
    public async Task Missing_permission_shows_banners_on_connect_before_any_scan()
    {
        _granted = ["GroupMember.Read.All", "Member.Read.Hidden", "AuditLog.Read.All"];
        var vm = CreateConnectableViewModel();

        await vm.ConnectCommand.ExecuteAsync(null);

        var banner = CheckPage(vm).Banner;
        Assert.True(banner.IsOpen);
        Assert.Equal(
            "This check needs the Microsoft Graph application permission Application.Read.All, which isn't granted " +
            "to this app registration. Add it and grant admin consent, then reconnect.",
            banner.Message);
        Assert.True(vm.Overview.Banner.IsOpen);
        Assert.Equal(
            $"Application.Read.All: needed by {CheckPage(vm).Title}, Expiring SAML certificates",
            Assert.Single(vm.Overview.Banner.Details));
        Assert.True(Assert.Single(AppCheckRow(vm).Permissions).IsMissing);
        Assert.True(vm.ChecksSettingsItem.HasWarning);
        Assert.True(vm.ChecksSettingsItem.HasBadge);
    }

    [Fact]
    public async Task Missing_audit_log_permission_shows_the_banner_on_mfa_not_registered()
    {
        _granted = ["Application.Read.All", "GroupMember.Read.All", "Member.Read.Hidden"];
        var vm = CreateConnectableViewModel();

        await vm.ConnectCommand.ExecuteAsync(null);

        var page = (CheckPageViewModel)vm.MenuItems.SelectMany(i => i.Children)
            .Single(i => i.Page is CheckPageViewModel p && p.Check.Id == MfaCheckId).Page!;
        Assert.True(page.Banner.IsOpen);
        Assert.Equal(["AuditLog.Read.All"], page.Banner.MissingPermissions);
        Assert.Equal(
            "AuditLog.Read.All: needed by MFA not registered",
            Assert.Single(vm.Overview.Banner.Details));
        Assert.False(CheckPage(vm).Banner.IsOpen);
    }

    [Fact]
    public async Task Unlicensed_check_shows_not_available_without_banner_badge_or_problem()
    {
        _licence = TenantLicence.Free;
        _granted = ["Application.Read.All", "GroupMember.Read.All", "Member.Read.Hidden", "LicenseAssignment.Read.All"];
        var vm = CreateConnectableViewModel();

        await vm.ConnectCommand.ExecuteAsync(null);

        var (item, page) = MfaItemAndPage(vm);
        Assert.False(item.IsEnabled);
        Assert.Equal("Needs an Entra ID P1 or P2 licence.", item.ToolTip);
        Assert.True(CheckItem(vm).IsEnabled);
        vm.SelectedNavItem = item;
        Assert.Same(vm.OverviewItem, vm.SelectedNavItem);
        Assert.False(page.Banner.IsOpen);
        Assert.Equal("Not available: needs an Entra ID P1 or P2 licence.", page.StatusText);
        Assert.False(vm.Overview.Banner.IsOpen);
        Assert.False(vm.ChecksSettingsItem.HasWarning);
        Assert.False(vm.ChecksSettings.Rows.Single(r => r.Check.Id == MfaCheckId).IsLicensed);
        Assert.False(vm.ExcessPermissions.HasUnusedReadPermissions);

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal("Not available: needs an Entra ID P1 or P2 licence.", page.StatusText);
        Assert.Equal(string.Empty, page.ErrorText);
        Assert.False(item.HasBadge);
        Assert.DoesNotContain(vm.Overview.Problems, p => p.Target == item);
        var users = vm.Overview.Categories.Single(c => c.Title == "Users");
        Assert.Equal((0, 0, 0), (users.Critical, users.Warning, users.Info));
        vm.Overview.OpenCommand.Execute(users.Target);
        Assert.Same(vm.OverviewItem, vm.SelectedNavItem);
    }

    [Fact]
    public async Task Scan_is_not_available_on_an_unlicensed_check_page()
    {
        _licence = TenantLicence.Free;
        var vm = CreateConnectableViewModel();
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.True(vm.ScanCommand.CanExecute(null));

        // The page can't be opened from the navigation; this covers it being current anyway.
        vm.CurrentPage = MfaItemAndPage(vm).Page;
        Assert.False(vm.ScanCommand.CanExecute(null));

        vm.CurrentPage = CheckPage(vm);
        Assert.True(vm.ScanCommand.CanExecute(null));
    }

    [Fact]
    public async Task Disconnecting_puts_back_the_connect_first_tooltip_on_an_unlicensed_check()
    {
        _licence = TenantLicence.Free;
        var vm = CreateConnectableViewModel();
        await vm.ConnectCommand.ExecuteAsync(null);

        vm.DisconnectCommand.Execute(null);

        var (item, _) = MfaItemAndPage(vm);
        Assert.False(item.IsEnabled);
        Assert.Equal(NavItemViewModel.ConnectFirstToolTip, item.ToolTip);
    }

    [Fact]
    public async Task Licensed_tenant_runs_the_licence_gated_check()
    {
        _licence = TenantLicence.EntraIdP1;
        var vm = CreateConnectableViewModel();
        await vm.ConnectCommand.ExecuteAsync(null);

        await vm.ScanCommand.ExecuteAsync(null);

        var (item, page) = MfaItemAndPage(vm);
        Assert.StartsWith("Last run", page.StatusText);
        Assert.True(item.HasBadge);
        Assert.Equal(0, item.BadgeValue);
    }

    [Fact]
    public async Task Granted_permissions_hide_the_banners()
    {
        var vm = CreateConnectableViewModel();

        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.False(CheckPage(vm).Banner.IsOpen);
        Assert.False(vm.Overview.Banner.IsOpen);
        Assert.True(Assert.Single(AppCheckRow(vm).Permissions).IsGranted);
        Assert.False(vm.ChecksSettingsItem.HasBadge);
    }

    [Fact]
    public async Task Banner_buttons_open_the_api_permissions_page_and_copy_the_names()
    {
        _granted = ["GroupMember.Read.All", "Member.Read.Hidden", "AuditLog.Read.All"];
        var vm = CreateConnectableViewModel();
        await vm.ConnectCommand.ExecuteAsync(null);

        await CheckPage(vm).Banner.OpenAdminCenterCommand.ExecuteAsync(null);
        await vm.Overview.Banner.CopyPermissionNamesCommand.ExecuteAsync(null);

        var url = Assert.Single(_prompt.OpenedUrls);
        Assert.Equal(EntraAdminCenter.ApiPermissionsUrl(ClientId), url);
        Assert.StartsWith(EntraAdminCenter.BaseUrl, url);
        Assert.Contains(ClientId, url);
        Assert.Equal("Application.Read.All", Assert.Single(_prompt.CopiedText));
        Assert.Same(vm.ReconnectCommand, CheckPage(vm).Banner.ReconnectCommand);
    }

    [Fact]
    public async Task Disabling_a_check_removes_it_from_the_overview_banner_and_is_saved()
    {
        _granted = ["GroupMember.Read.All", "Member.Read.Hidden", "AuditLog.Read.All"];
        var vm = CreateConnectableViewModel();
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.True(vm.Overview.Banner.IsOpen);

        AppCheckRow(vm).IsEnabled = false;

        Assert.Equal(
            "Application.Read.All: needed by Expiring SAML certificates",
            Assert.Single(vm.Overview.Banner.Details));

        vm.ChecksSettings.Rows.Single(r => r.Check.Id == SamlCheckId).IsEnabled = false;

        Assert.False(vm.Overview.Banner.IsOpen);
        Assert.False(vm.ChecksSettingsItem.HasWarning);
        Assert.True(CheckPage(vm).IsDisabled);
        Assert.Equal(
            new[] { CheckId, SamlCheckId },
            new SettingsStore(_dir).Load(out _).DisabledCheckIds.Order());
    }

    [Fact]
    public async Task Unneeded_write_and_read_permissions_show_the_excess_banners()
    {
        _granted = ["Application.Read.All", "User.ReadWrite.All", "Mail.Send", "Directory.Read.All"];
        var vm = CreateConnectableViewModel();

        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.True(vm.ExcessPermissions.HasWritePermissions);
        Assert.Contains("Mail.Send, User.ReadWrite.All", vm.ExcessPermissions.WriteMessage);
        Assert.Contains("Anyone with its certificate can change your tenant", vm.ExcessPermissions.WriteMessage);
        Assert.True(vm.ExcessPermissions.HasUnusedReadPermissions);
        Assert.Contains("Directory.Read.All", vm.ExcessPermissions.ReadMessage);
        Assert.True(vm.About.IsConnected);

        vm.DisconnectCommand.Execute(null);

        Assert.False(vm.ExcessPermissions.HasWritePermissions);
        Assert.False(vm.About.IsConnected);
    }

    [Fact]
    public async Task Overview_counts_findings_per_category_and_opens_the_check()
    {
        var vm = CreateConnectableViewModel();
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.False(vm.Overview.HasScan);

        await vm.ScanCommand.ExecuteAsync(null);

        var category = vm.Overview.Categories.Single(c => c.Title == "App registrations");
        Assert.Equal((1, 0, 0), (category.Critical, category.Warning, category.Info));
        Assert.False(vm.Overview.HasProblems);

        vm.Overview.OpenCommand.Execute(category.Target);

        Assert.Same(CheckItem(vm), vm.SelectedNavItem);
        Assert.Same(CheckPage(vm), vm.CurrentPage);
    }

    [Fact]
    public async Task Overview_lists_checks_that_failed_with_the_reason()
    {
        _reader = new FailingReader();
        var vm = CreateConnectableViewModel();
        await vm.ConnectCommand.ExecuteAsync(null);

        await vm.ScanCommand.ExecuteAsync(null);

        var problem = vm.Overview.Problems.Single(p => p.Target == CheckItem(vm));
        Assert.Contains("fake read failure", problem.Reason);
    }

    [Fact]
    public async Task Overview_gives_the_licence_message_for_a_tenant_without_the_licence()
    {
        _reader = new FailingReader("Authentication_RequestFromNonPremiumTenantOrB2CTenant");
        var vm = CreateConnectableViewModel();
        await vm.ConnectCommand.ExecuteAsync(null);

        await vm.ScanCommand.ExecuteAsync(null);

        var problem = vm.Overview.Problems.Single(p => p.Target == CheckItem(vm));
        Assert.Equal("Failed: This check needs an Entra ID P1 or P2 licence.", problem.Reason);
    }

    [Fact]
    public async Task Local_time_preference_is_saved_and_reformats_shown_times()
    {
        var vm = CreateConnectableViewModel();
        await vm.ConnectCommand.ExecuteAsync(null);
        await vm.ScanCommand.ExecuteAsync(null);
        Assert.Equal("Last scan: 2026-01-15 12:00 UTC", vm.Overview.LastScanText);

        vm.Preferences.UseLocalTime = true;

        Assert.Equal("Last scan: 2026-01-15 14:00", vm.Overview.LastScanText);
        Assert.Equal("2026-01-15 14:00", vm.Overview.ConnectedAtText);
        Assert.Equal("Expires (local)", CheckPage(vm).DueHeader);
        Assert.True(new SettingsStore(_dir).Load(out _).UseLocalTime);
    }

    [Fact]
    public async Task Connection_status_is_shown_as_name_and_tenant()
    {
        var vm = CreateConnectableViewModel();

        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.Equal("p1 (t1)", vm.ConnectedProfileTitle);
        vm.DisconnectCommand.Execute(null);
        Assert.Equal(string.Empty, vm.ConnectedProfileTitle);
    }

    private MainViewModel CreateConnectableViewModel()
    {
        SaveProfiles(new ConnectionProfile { Name = "p1", TenantId = "t1", ClientId = ClientId, CertificateThumbprint = "AB" });
        return CreateViewModel(lastUsed: "p1");
    }

    private MainViewModel CreateViewModel(string? lastUsed = null)
    {
        if (lastUsed is not null && !File.Exists(Path.Combine(_dir, "settings.json")))
        {
            new SettingsStore(_dir).Save(new AppSettings { LastUsedProfileName = lastUsed });
        }

        return new MainViewModel(new ProfileStore(_dir), new SettingsStore(_dir), Connect, _prompt, _time, PlusTwoHours);
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
            TenantLicence = _licence,
        });
    }

    private void SaveProfiles(params ConnectionProfile[] profiles) => new ProfileStore(_dir).Save(profiles);

    private static NavItemViewModel CheckItem(MainViewModel vm) =>
        vm.MenuItems.SelectMany(i => i.Children)
            .Single(i => i.Kind == NavItemKind.Check && ((CheckPageViewModel)i.Page!).Check.Id == CheckId);

    private static (NavItemViewModel Item, CheckPageViewModel Page) MfaItemAndPage(MainViewModel vm)
    {
        var item = vm.MenuItems.SelectMany(i => i.Children)
            .Single(i => i.Page is CheckPageViewModel p && p.Check.Id == MfaCheckId);
        return (item, (CheckPageViewModel)item.Page!);
    }

    private static CheckSettingsRow AppCheckRow(MainViewModel vm) => vm.ChecksSettings.Rows.Single(r => r.Check.Id == CheckId);

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

    private sealed class FakeReader(IReadOnlyList<AppRegistration> apps) : IDirectoryReader
    {
        public int Reads { get; private set; }

        public Task<IReadOnlyList<AppRegistration>> GetAppRegistrationsAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult(apps);
        }

        public Task<IReadOnlyList<DirectoryGroup>> GetGroupsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DirectoryGroup>>([]);

        public Task<IReadOnlyList<EnterpriseApp>> GetEnterpriseAppsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EnterpriseApp>>([]);

        public Task<IReadOnlyList<UserRegistration>> GetUserRegistrationsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UserRegistration>>([]);

        public Task<IReadOnlyList<AppPermissionGrant>> GetGraphApplicationPermissionGrantsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AppPermissionGrant>>([]);
    }

    private sealed class FailingReader(string? errorCode = null) : IDirectoryReader
    {
        public Task<IReadOnlyList<AppRegistration>> GetAppRegistrationsAsync(CancellationToken cancellationToken = default) =>
            throw Failure();

        public Task<IReadOnlyList<DirectoryGroup>> GetGroupsAsync(CancellationToken cancellationToken = default) =>
            throw Failure();

        public Task<IReadOnlyList<EnterpriseApp>> GetEnterpriseAppsAsync(CancellationToken cancellationToken = default) =>
            throw Failure();

        public Task<IReadOnlyList<UserRegistration>> GetUserRegistrationsAsync(CancellationToken cancellationToken = default) =>
            throw Failure();

        public Task<IReadOnlyList<AppPermissionGrant>> GetGraphApplicationPermissionGrantsAsync(CancellationToken cancellationToken = default) =>
            throw Failure();

        private DirectoryReadException Failure() =>
            new("fake read failure", errorCode is null ? 500 : 403, errorCode: errorCode);
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

        public async Task<IReadOnlyList<DirectoryGroup>> GetGroupsAsync(CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return [];
        }

        public async Task<IReadOnlyList<EnterpriseApp>> GetEnterpriseAppsAsync(CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return [];
        }

        public async Task<IReadOnlyList<UserRegistration>> GetUserRegistrationsAsync(CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return [];
        }

        public async Task<IReadOnlyList<AppPermissionGrant>> GetGraphApplicationPermissionGrantsAsync(CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return [];
        }
    }
}
