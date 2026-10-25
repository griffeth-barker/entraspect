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

    private const string AppNoOwnersCheckId = "app-no-owners";

    private const string RedirectUriCheckId = "app-risky-redirect-uris";

    private const string HighPrivilegeCheckId = "sp-high-privilege-app-permissions";

    private const string MfaCheckId = "users-mfa-not-registered";
    private const string MfaRequiredCheckId = "users-mfa-not-required";
    private const string CaPolicyCheckId = "ca-policy-hygiene";
    private const string CaUnusedCheckId = "ca-unused-policies";
    private const string FakePassword = "fake-pfx-password-for-test";
    private const string ClientId = "00000000-0000-0000-0000-0000000000c1";

    private static readonly DateTimeOffset Now = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"entraspect-main-{Guid.NewGuid()}");
    private readonly FakeTimeProvider _time = new(Now);
    private readonly FakeUserInteraction _prompt = new();
    private readonly List<(ConnectionProfile Profile, string? Password)> _connectCalls = [];

    private IDirectoryReader _reader = new FakeReader(OneExpiringApp());
    private IReadOnlyList<string> _granted =
        ["Application.Read.All", "GroupMember.Read.All", "Member.Read.Hidden", "AuditLog.Read.All", "Policy.Read.All", "User.Read.All", "RoleManagement.Read.Directory"];
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
            new[] { "Users", "Groups", "App registrations", "Enterprise apps", "Conditional Access", "External Identities" },
            vm.MenuItems.Skip(1).Select(i => i.Title));
        Assert.Equal(
            new[] { MfaCheckId, MfaRequiredCheckId },
            vm.MenuItems[1].Children.Select(c => Assert.IsType<CheckPageViewModel>(c.Page).Check.Id));
        Assert.Equal(
            new[]
            {
                "group-no-members", "group-no-owners", "group-single-owner", "group-license-errors",
                "group-dynamic-paused", "group-public", "group-sync-errors",
            },
            vm.MenuItems[2].Children.Select(c => Assert.IsType<CheckPageViewModel>(c.Page).Check.Id));
        var section = vm.MenuItems[3];
        Assert.False(section.IsPage);
        Assert.False(section.IsExpanded);
        Assert.Equal(
            new[] { AppNoOwnersCheckId, CheckId, RedirectUriCheckId },
            section.Children.Select(c => Assert.IsType<CheckPageViewModel>(c.Page).Check.Id));
        Assert.Equal(
            new[] { SamlCheckId, HighPrivilegeCheckId },
            vm.MenuItems[4].Children.Select(c => Assert.IsType<CheckPageViewModel>(c.Page).Check.Id));
        Assert.Equal(
            new[] { CaPolicyCheckId, CaUnusedCheckId },
            vm.MenuItems[5].Children.Select(c => Assert.IsType<CheckPageViewModel>(c.Page).Check.Id));

        // External Identities is last, as in the Entra admin center, and collapsible like every section.
        var external = vm.MenuItems[^1];
        Assert.Equal("Share", external.Icon);
        Assert.Single(vm.MenuItems, i => i.Icon == external.Icon);
        Assert.False(external.IsPage);
        Assert.False(external.IsExpanded);
        Assert.Equal(
            new[] { "cross-tenant-access-defaults", "cross-tenant-access-partners", "external-collaboration-settings" },
            external.Children.Select(c => Assert.IsType<CheckPageViewModel>(c.Page).Check.Id));
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

        AssertNoBadge(CheckItem(vm));
        await vm.ScanCommand.ExecuteAsync(null);

        var item = CheckItem(vm);
        Assert.Equal(1, item.BadgeCount);
        Assert.Equal(Severity.Critical, item.BadgeSeverity);
        Assert.Equal("1 critical", item.CountToolTip);
        Assert.False(item.HasProblem);
        Assert.True(item.ShowBadges);
        Assert.Single(CheckPage(vm).Findings);
        Assert.False(vm.IsScanning);
        Assert.StartsWith("Scan finished", vm.StatusBar.ActivityText);
        Assert.True(vm.Overview.HasScan);
    }

    [Fact]
    public async Task The_status_bar_follows_the_connection_the_scan_and_the_page()
    {
        var vm = CreateConnectableViewModel();
        Assert.Equal(StatusBarViewModel.NotConnectedText, vm.StatusBar.ActivityText);
        Assert.False(vm.StatusBar.HasProfile);

        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.Equal(StatusBarViewModel.ConnectedText, vm.StatusBar.ActivityText);
        Assert.Equal("p1", vm.StatusBar.ProfileName);
        Assert.Equal(string.Empty, vm.StatusBar.ContextText);
        Assert.Same(vm.CancelCommand, vm.StatusBar.CancelCommand);

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal("Scan finished at 12:00 UTC", vm.StatusBar.ActivityText);
        Assert.False(vm.StatusBar.IsScanning);
        Assert.Equal("Last scan 2026-01-15 12:00 UTC", vm.StatusBar.ContextText);

        vm.SelectedNavItem = CheckItem(vm);
        Assert.Equal("Last run 2026-01-15 12:00 UTC", vm.StatusBar.ContextText);

        vm.SelectedNavItem = vm.ChecksSettingsItem;
        Assert.Equal(string.Empty, vm.StatusBar.ContextText);
        Assert.Equal("p1", vm.StatusBar.ProfileName);

        vm.DisconnectCommand.Execute(null);

        Assert.Equal(StatusBarViewModel.NotConnectedText, vm.StatusBar.ActivityText);
        Assert.False(vm.StatusBar.HasProfile);
        Assert.Equal(string.Empty, vm.StatusBar.ContextText);
    }

    [Fact]
    public async Task A_failed_connect_leaves_the_status_bar_not_connected()
    {
        _connectError = new DirectoryReadException("Sign-in failed: fake test error");
        var vm = CreateConnectableViewModel();

        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.Equal(StatusBarViewModel.NotConnectedText, vm.StatusBar.ActivityText);
        Assert.False(vm.StatusBar.IsProgressVisible);
    }

    [Fact]
    public async Task A_collapsed_section_sums_its_checks_and_an_expanded_one_shows_no_badge()
    {
        var vm = CreateConnectableViewModel();
        await vm.ConnectCommand.ExecuteAsync(null);
        var section = vm.MenuItems.Single(i => i.Title == "App registrations");
        Assert.False(section.IsExpanded);

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal(1, section.BadgeCount);
        Assert.Equal(Severity.Critical, section.BadgeSeverity);
        Assert.Equal("1 critical", section.CountToolTip);
        Assert.False(section.HasProblem);

        vm.SelectedNavItem = CheckItem(vm);

        Assert.True(section.IsExpanded);
        AssertNoBadge(section);
    }

    [Fact]
    public async Task Export_is_disabled_until_a_scan_has_finished()
    {
        var vm = CreateConnectableViewModel();
        Assert.False(vm.ExportCommand.CanExecute(null));

        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Same(vm.Overview, vm.CurrentPage);
        Assert.False(vm.ExportCommand.CanExecute(null));

        await vm.ScanCommand.ExecuteAsync(null);
        Assert.True(vm.ExportCommand.CanExecute(null));

        vm.SelectedNavItem = vm.ChecksSettingsItem;
        Assert.False(vm.ExportCommand.CanExecute(null));

        vm.SelectedNavItem = CheckItem(vm);
        Assert.True(vm.ExportCommand.CanExecute(null));

        vm.DisconnectCommand.Execute(null);
        Assert.False(vm.ExportCommand.CanExecute(null));
    }

    [Fact]
    public async Task Export_on_a_check_page_whose_check_did_not_run_is_disabled()
    {
        new SettingsStore(_dir).Save(new AppSettings { DisabledCheckIds = [CheckId], LastUsedProfileName = "p1" });
        var vm = CreateConnectableViewModel();
        await vm.ConnectCommand.ExecuteAsync(null);
        await vm.ScanCommand.ExecuteAsync(null);

        vm.SelectedNavItem = CheckItem(vm);

        Assert.False(vm.ExportCommand.CanExecute(null));
    }

    [Fact]
    public async Task Export_on_overview_writes_every_check_of_the_last_scan()
    {
        var output = new MemoryStream();
        _prompt.ExportFile = new ExportFile("scan.json", ExportFormat.Json, () => Task.FromResult<Stream>(output));
        var vm = CreateConnectableViewModel();
        await vm.ConnectCommand.ExecuteAsync(null);
        await vm.ScanCommand.ExecuteAsync(null);

        await vm.ExportCommand.ExecuteAsync(null);

        Assert.Equal(["entraspect-p1-20260115-1200.csv"], _prompt.SuggestedFileNames);
        var report = System.Text.Json.JsonSerializer.Deserialize<ScanReport>(
            output.ToArray(), JsonScanExporter.SerializerOptions)!;
        Assert.Equal("p1", report.ProfileName);
        Assert.Equal("t1", report.TenantId);
        Assert.Equal(Now, report.ScannedAt);
        Assert.Equal(vm.About.Version, report.AppVersion);
        Assert.Equal(vm.Checks.Select(c => c.Id), report.Checks.Select(c => c.CheckId));
        Assert.Single(report.Checks.Single(c => c.CheckId == CheckId).Findings);
        Assert.Equal("Exported to scan.json", vm.StatusBar.ActivityText);
        Assert.Equal(string.Empty, vm.ExportErrorText);
    }

    [Fact]
    public async Task Export_on_a_check_page_writes_only_the_findings_shown()
    {
        var output = new MemoryStream();
        _prompt.ExportFile = new ExportFile("app.csv", ExportFormat.Csv, () => Task.FromResult<Stream>(output));
        var vm = CreateConnectableViewModel();
        await vm.ConnectCommand.ExecuteAsync(null);
        await vm.ScanCommand.ExecuteAsync(null);
        vm.SelectedNavItem = CheckItem(vm);

        CheckPage(vm).SearchText = "no such app";
        await vm.ExportCommand.ExecuteAsync(null);
        var filtered = System.Text.Encoding.UTF8.GetString(output.ToArray()).TrimStart('\uFEFF').Split("\r\n");

        output = new MemoryStream();
        CheckPage(vm).SearchText = "Fake app";
        await vm.ExportCommand.ExecuteAsync(null);
        var shown = System.Text.Encoding.UTF8.GetString(output.ToArray()).TrimStart('\uFEFF').Split("\r\n");

        Assert.Equal(2, filtered.Length);
        Assert.StartsWith("CheckId,", filtered[0]);
        Assert.Equal(3, shown.Length);
        Assert.StartsWith($"{CheckId},", shown[1]);
        Assert.Contains("Fake app", shown[1]);
    }

    [Fact]
    public async Task Export_cancelled_in_the_dialog_writes_nothing()
    {
        _prompt.ExportFile = null;
        var vm = CreateConnectableViewModel();
        await vm.ConnectCommand.ExecuteAsync(null);
        await vm.ScanCommand.ExecuteAsync(null);

        await vm.ExportCommand.ExecuteAsync(null);

        Assert.Single(_prompt.SuggestedFileNames);
        Assert.StartsWith("Scan finished", vm.StatusBar.ActivityText);
        Assert.Equal(string.Empty, vm.ExportErrorText);
    }

    [Fact]
    public async Task Export_write_error_is_shown_plainly()
    {
        _prompt.ExportFile = new ExportFile(
            "scan.csv", ExportFormat.Csv, () => throw new IOException("The disk is full."));
        var vm = CreateConnectableViewModel();
        await vm.ConnectCommand.ExecuteAsync(null);
        await vm.ScanCommand.ExecuteAsync(null);

        await vm.ExportCommand.ExecuteAsync(null);

        Assert.Equal("Couldn't export to scan.csv: The disk is full.", vm.ExportErrorText);

        await vm.ScanCommand.ExecuteAsync(null);
        Assert.Equal(string.Empty, vm.ExportErrorText);
    }

    [Fact]
    public async Task Export_file_name_follows_the_time_preference()
    {
        var vm = CreateConnectableViewModel();
        await vm.ConnectCommand.ExecuteAsync(null);
        await vm.ScanCommand.ExecuteAsync(null);
        SelectTimeZone(vm, AppSettings.LocalTimeZone);

        await vm.ExportCommand.ExecuteAsync(null);

        Assert.Equal(["entraspect-p1-20260115-1400.csv"], _prompt.SuggestedFileNames);
    }

    [Fact]
    public async Task A_check_missing_permissions_shows_a_warning_marker()
    {
        _granted = [];
        var vm = CreateConnectableViewModel();
        await vm.ConnectCommand.ExecuteAsync(null);

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.True(CheckItem(vm).HasProblem);
        Assert.Equal(NavItemViewModel.MissingPermissionsToolTip, CheckItem(vm).ProblemToolTip);
        Assert.Equal(0, CheckItem(vm).BadgeCount);
        Assert.Equal(["Application.Read.All"], CheckPage(vm).Banner.MissingPermissions);
    }

    [Fact]
    public async Task A_failed_check_shows_a_warning_marker()
    {
        _reader = new FailingReader();
        var vm = CreateConnectableViewModel();
        await vm.ConnectCommand.ExecuteAsync(null);

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.True(CheckItem(vm).HasProblem);
        Assert.Equal(NavItemViewModel.FailedToolTip, CheckItem(vm).ProblemToolTip);
        Assert.Contains("fake read failure", CheckPage(vm).ErrorText);
    }

    [Fact]
    public async Task A_disabled_check_is_not_run_and_has_no_badge()
    {
        new SettingsStore(_dir).Save(new AppSettings
        {
            DisabledCheckIds = [CheckId, AppNoOwnersCheckId, RedirectUriCheckId],
            LastUsedProfileName = "p1",
        });
        var vm = CreateConnectableViewModel();
        await vm.ConnectCommand.ExecuteAsync(null);

        await vm.ScanCommand.ExecuteAsync(null);

        AssertNoBadge(CheckItem(vm));
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
        Assert.True(vm.StatusBar.IsScanning);
        Assert.True(vm.StatusBar.IsProgressVisible);
        vm.StatusBar.CancelCommand.Execute(null);
        await scan.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(vm.IsScanning);
        Assert.Equal(StatusBarViewModel.CancelledText, vm.StatusBar.ActivityText);
        Assert.False(vm.StatusBar.IsScanning);
        AssertNoBadge(CheckItem(vm));
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
        AssertNoBadge(CheckItem(vm));
        Assert.Empty(CheckPage(vm).Findings);
        Assert.False(vm.Overview.HasScan);
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
        AssertNoBadge(CheckItem(vm));
    }

    [Fact]
    public async Task Missing_permission_shows_banners_on_connect_before_any_scan()
    {
        _granted = ["GroupMember.Read.All", "Member.Read.Hidden", "AuditLog.Read.All", "Policy.Read.All", "User.Read.All", "RoleManagement.Read.Directory"];
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
            $"Application.Read.All: needed by Apps with no owners, {CheckPage(vm).Title}, Risky redirect URIs, " +
            "Expiring SAML certificates, High-privilege apps",
            Assert.Single(vm.Overview.Banner.Details));
        Assert.True(Assert.Single(AppCheckRow(vm).Permissions).IsMissing);
        Assert.True(vm.ChecksSettingsItem.HasProblem);
        Assert.Equal(NavItemViewModel.MissingPermissionsWarning, vm.ChecksSettingsItem.ProblemToolTip);
    }

    [Fact]
    public async Task Missing_audit_log_permission_shows_the_banner_on_mfa_not_registered()
    {
        _granted = ["Application.Read.All", "GroupMember.Read.All", "Member.Read.Hidden", "Policy.Read.All", "User.Read.All", "RoleManagement.Read.Directory"];
        var vm = CreateConnectableViewModel();

        await vm.ConnectCommand.ExecuteAsync(null);

        var page = (CheckPageViewModel)vm.MenuItems.SelectMany(i => i.Children)
            .Single(i => i.Page is CheckPageViewModel p && p.Check.Id == MfaCheckId).Page!;
        Assert.True(page.Banner.IsOpen);
        Assert.Equal(["AuditLog.Read.All"], page.Banner.MissingPermissions);
        Assert.Equal(
            "AuditLog.Read.All: needed by MFA not registered, MFA not required, Unused policies",
            Assert.Single(vm.Overview.Banner.Details));
        Assert.False(CheckPage(vm).Banner.IsOpen);
    }

    [Fact]
    public void Every_navigation_item_has_a_known_icon()
    {
        var vm = CreateViewModel();

        foreach (var item in vm.MenuItems.Concat(vm.FooterItems).SelectMany(i => i.Children.Prepend(i)))
        {
            if (item.Kind == NavItemKind.Check)
            {
                continue;
            }

            Assert.True(
                Enum.TryParse<FluentAvalonia.UI.Controls.FASymbol>(item.Icon, out _),
                $"'{item.Title}' has icon '{item.Icon}', which isn't a FASymbol.");
        }
    }

    [Fact]
    public async Task Missing_policy_and_user_permissions_show_the_banner_on_policy_hygiene()
    {
        _granted = ["Application.Read.All", "GroupMember.Read.All", "Member.Read.Hidden", "AuditLog.Read.All", "RoleManagement.Read.Directory"];
        var vm = CreateConnectableViewModel();

        await vm.ConnectCommand.ExecuteAsync(null);

        var page = (CheckPageViewModel)vm.MenuItems.SelectMany(i => i.Children)
            .Single(i => i.Page is CheckPageViewModel p && p.Check.Id == CaPolicyCheckId).Page!;
        Assert.True(page.Banner.IsOpen);
        Assert.Equal(["Policy.Read.All", "User.Read.All"], page.Banner.MissingPermissions);
        Assert.Equal(
            [
                "Policy.Read.All: needed by MFA not required, Policy hygiene, Unused policies, Cross-tenant defaults, " +
                "Cross-tenant partners, External collaboration",
                "User.Read.All: needed by MFA not required, Policy hygiene",
            ],
            vm.Overview.Banner.Details);
        Assert.False(CheckPage(vm).Banner.IsOpen);
    }

    [Fact]
    public async Task Missing_role_management_permission_shows_the_banner_on_mfa_not_required()
    {
        _granted = ["Application.Read.All", "GroupMember.Read.All", "Member.Read.Hidden", "AuditLog.Read.All", "Policy.Read.All", "User.Read.All"];
        var vm = CreateConnectableViewModel();

        await vm.ConnectCommand.ExecuteAsync(null);

        var page = (CheckPageViewModel)vm.MenuItems.SelectMany(i => i.Children)
            .Single(i => i.Page is CheckPageViewModel p && p.Check.Id == MfaRequiredCheckId).Page!;
        Assert.True(page.Banner.IsOpen);
        Assert.Equal(["RoleManagement.Read.Directory"], page.Banner.MissingPermissions);
        Assert.Equal(
            "RoleManagement.Read.Directory: needed by MFA not required",
            Assert.Single(vm.Overview.Banner.Details));
        Assert.False(CheckPage(vm).Banner.IsOpen);
    }

    [Fact]
    public async Task Unlicensed_check_shows_not_available_without_banner_badge_or_problem()
    {
        _licence = TenantLicence.Free;
        _granted = ["Application.Read.All", "GroupMember.Read.All", "Member.Read.Hidden", "LicenseAssignment.Read.All", "Policy.Read.All", "User.Read.All", "RoleManagement.Read.Directory"];
        var vm = CreateConnectableViewModel();

        await vm.ConnectCommand.ExecuteAsync(null);

        var (item, page) = MfaItemAndPage(vm);
        Assert.False(item.IsEnabled);
        Assert.Equal("Needs an Entra ID P1 or P2 licence.", item.ToolTip);
        Assert.True(CheckItem(vm).IsEnabled);
        vm.SelectedNavItem = item;
        Assert.Same(vm.OverviewItem, vm.SelectedNavItem);
        Assert.False(page.Banner.IsOpen);
        Assert.Equal("Not available: needs an Entra ID P1 or P2 licence.", page.NotAvailableText);
        Assert.Equal(string.Empty, page.StatusText);
        Assert.False(vm.Overview.Banner.IsOpen);
        Assert.False(vm.ChecksSettingsItem.HasProblem);
        Assert.False(vm.ChecksSettings.Rows.Single(r => r.Check.Id == MfaCheckId).IsLicensed);
        Assert.False(vm.ExcessPermissions.HasUnusedReadPermissions);

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, page.StatusText);
        Assert.Equal(string.Empty, page.ErrorText);
        AssertNoBadge(item);
        Assert.DoesNotContain(vm.Overview.Problems, p => p.Target == item);
        var users = vm.Overview.Categories.Single(c => c.Title == "Users");
        Assert.False(users.IsAvailable);
        Assert.Equal("Needs an Entra ID P1 or P2 licence.", users.UnavailableReason);
        Assert.Equal(("\u2013", "\u2013", "\u2013"), (users.CriticalText, users.WarningText, users.InfoText));
        Assert.False(vm.Overview.Categories.Single(c => c.Title == "Conditional Access").IsAvailable);
        Assert.All(
            vm.Overview.Categories.Where(c => c.Title is not "Users" and not "Conditional Access"),
            c => Assert.True(c.IsAvailable));
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
        AssertNoBadge(item);
        var users = vm.Overview.Categories.Single(c => c.Title == "Users");
        Assert.True(users.IsAvailable);
        Assert.Equal("0", users.CriticalText);
    }

    [Fact]
    public async Task Granted_permissions_hide_the_banners()
    {
        var vm = CreateConnectableViewModel();

        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.False(CheckPage(vm).Banner.IsOpen);
        Assert.False(vm.Overview.Banner.IsOpen);
        Assert.True(Assert.Single(AppCheckRow(vm).Permissions).IsGranted);
        Assert.False(vm.ChecksSettingsItem.HasProblem);
    }

    [Fact]
    public async Task Banner_buttons_open_the_api_permissions_page_and_copy_the_names()
    {
        _granted = ["GroupMember.Read.All", "Member.Read.Hidden", "AuditLog.Read.All", "Policy.Read.All", "User.Read.All", "RoleManagement.Read.Directory"];
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
        _granted = ["GroupMember.Read.All", "Member.Read.Hidden", "AuditLog.Read.All", "Policy.Read.All", "User.Read.All", "RoleManagement.Read.Directory"];
        var vm = CreateConnectableViewModel();
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.True(vm.Overview.Banner.IsOpen);

        AppCheckRow(vm).IsEnabled = false;

        Assert.Equal(
            "Application.Read.All: needed by Apps with no owners, Risky redirect URIs, Expiring SAML certificates, " +
            "High-privilege apps",
            Assert.Single(vm.Overview.Banner.Details));

        foreach (var id in new[] { AppNoOwnersCheckId, RedirectUriCheckId, SamlCheckId, HighPrivilegeCheckId })
        {
            vm.ChecksSettings.Rows.Single(r => r.Check.Id == id).IsEnabled = false;
        }

        Assert.False(vm.Overview.Banner.IsOpen);
        Assert.False(vm.ChecksSettingsItem.HasProblem);
        Assert.True(CheckPage(vm).IsDisabled);
        Assert.Equal(
            new[] { CheckId, AppNoOwnersCheckId, RedirectUriCheckId, SamlCheckId, HighPrivilegeCheckId }.Order(),
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

        // The section opens at its first check, in title order.
        var first = vm.MenuItems.Single(i => i.Title == "App registrations").Children[0];
        Assert.Equal(AppNoOwnersCheckId, ((CheckPageViewModel)first.Page!).Check.Id);
        Assert.Same(first, vm.SelectedNavItem);
        Assert.Same(first.Page, vm.CurrentPage);
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
        Assert.Equal("Last scan 2026-01-15 12:00 UTC", vm.StatusBar.ContextText);

        SelectTimeZone(vm, AppSettings.LocalTimeZone);

        Assert.Equal("Scan finished at 14:00", vm.StatusBar.ActivityText);
        Assert.Equal("Last scan 2026-01-15 14:00", vm.StatusBar.ContextText);
        vm.SelectedNavItem = CheckItem(vm);
        Assert.Equal("Last run 2026-01-15 14:00", vm.StatusBar.ContextText);
        Assert.Equal("2026-01-15 14:00", vm.Overview.ConnectedAtText);
        Assert.Equal("Expires (local)", CheckPage(vm).DueHeader);
        Assert.Equal(AppSettings.LocalTimeZone, new SettingsStore(_dir).Load(out _).TimeZone);
    }

    [Fact]
    public async Task A_chosen_time_zone_is_saved_and_reformats_shown_times()
    {
        var vm = CreateConnectableViewModel();
        await vm.ConnectCommand.ExecuteAsync(null);
        await vm.ScanCommand.ExecuteAsync(null);

        SelectTimeZone(vm, "Asia/Tokyo");

        Assert.Equal("Last scan 2026-01-15 21:00 (UTC+09:00)", vm.StatusBar.ContextText);
        Assert.Equal("2026-01-15 21:00 (UTC+09:00)", vm.Overview.ConnectedAtText);
        Assert.Equal("Expires (Asia/Tokyo)", CheckPage(vm).DueHeader);
        Assert.Equal("Asia/Tokyo", new SettingsStore(_dir).Load(out _).TimeZone);
    }

    [Fact]
    public void The_saved_theme_is_applied_at_startup()
    {
        new SettingsStore(_dir).Save(new AppSettings { Theme = AppTheme.Light });

        CreateViewModel();

        Assert.Equal([AppTheme.Light], _prompt.Themes);
    }

    [Fact]
    public async Task Open_settings_json_saves_a_missing_file_first()
    {
        var vm = CreateViewModel();

        await vm.About.OpenSettingsFileCommand.ExecuteAsync(null);

        Assert.Equal([(SettingsPath, true)], _prompt.OpenedFiles);
        Assert.Equal(string.Empty, vm.About.SettingsErrorText);
    }

    [Fact]
    public async Task Reload_settings_applies_checks_options_time_zone_and_theme_at_once()
    {
        var vm = CreateConnectableViewModel();
        await vm.ConnectCommand.ExecuteAsync(null);
        await vm.ScanCommand.ExecuteAsync(null);
        File.WriteAllText(SettingsPath, $$"""
            {
              "DisabledCheckIds": ["{{CheckId}}"],
              "CheckOptions": { "ExpiryWarningDays": 45, "ExpiryCriticalDays": 10 },
              "LastUsedProfileName": "p1",
              "TimeZone": "Asia/Tokyo",
              "Theme": "Dark"
            }
            """);
        var written = File.ReadAllText(SettingsPath);

        vm.About.ReloadSettingsCommand.Execute(null);

        Assert.Equal(string.Empty, vm.About.SettingsErrorText);
        Assert.Equal("Reloaded settings.json.", vm.About.SettingsStatusText);
        Assert.False(AppCheckRow(vm).IsEnabled);
        Assert.True(CheckPage(vm).IsDisabled);
        Assert.Equal(45, vm.ChecksSettings.ExpiryWarningDays);
        Assert.Equal(10, vm.ChecksSettings.ExpiryCriticalDays);
        Assert.Equal("Asia/Tokyo", vm.Preferences.SelectedTimeZone?.Id);
        Assert.Equal("Last scan 2026-01-15 21:00 (UTC+09:00)", vm.StatusBar.ContextText);
        Assert.Equal(AppTheme.Dark, vm.Preferences.SelectedTheme?.Theme);
        Assert.Equal(AppTheme.Dark, _prompt.Themes[^1]);
        Assert.Equal(written, File.ReadAllText(SettingsPath));
    }

    [Theory]
    [InlineData("{ not json", "settings.json isn't valid")]
    [InlineData("{\"CheckOptions\":{\"ExpiryWarningDays\":5,\"ExpiryCriticalDays\":10}}",
        "settings.json has invalid check options: Critical days must be less than or equal to warning days.")]
    [InlineData("{\"TimeZone\":\"Mars/Olympus_Mons\"}",
        "The time zone 'Mars/Olympus_Mons' isn't available on this computer.")]
    [InlineData("{\"Theme\":\"Purple\"}", "settings.json has an unknown theme.")]
    public void Reload_of_invalid_settings_shows_why_and_changes_nothing(string json, string expectedError)
    {
        new SettingsStore(_dir).Save(new AppSettings { TimeZone = "Europe/Berlin", Theme = AppTheme.Light });
        var vm = CreateViewModel();
        File.WriteAllText(SettingsPath, json);

        vm.About.ReloadSettingsCommand.Execute(null);

        Assert.StartsWith(expectedError, vm.About.SettingsErrorText);
        Assert.Equal(string.Empty, vm.About.SettingsStatusText);
        Assert.Equal("Europe/Berlin", vm.Preferences.SelectedTimeZone?.Id);
        Assert.Equal(AppTheme.Light, vm.Preferences.SelectedTheme?.Theme);
        Assert.Equal([AppTheme.Light], _prompt.Themes);
        Assert.True(AppCheckRow(vm).IsEnabled);
        Assert.Equal(json, File.ReadAllText(SettingsPath));
        Assert.DoesNotContain(Directory.GetFiles(_dir), f => f.Contains("corrupt"));
    }

    [Fact]
    public async Task The_connected_profiles_card_has_the_connection_buttons_and_connect_is_hidden()
    {
        var vm = CreateConnectableViewModel();
        Assert.True(vm.Profiles.ShowConnect);

        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.True(Assert.Single(vm.Profiles.Profiles).IsConnected);
        Assert.False(vm.Profiles.ShowConnect);
        Assert.False(vm.ShowStatusConnectionButtons);

        vm.DisconnectCommand.Execute(null);

        Assert.False(Assert.Single(vm.Profiles.Profiles).IsConnected);
        Assert.True(vm.Profiles.ShowConnect);
        Assert.False(vm.ShowStatusConnectionButtons);
    }

    [Fact]
    public async Task Without_a_card_for_the_connected_profile_the_status_card_keeps_the_buttons()
    {
        var vm = CreateConnectableViewModel();
        await vm.ConnectCommand.ExecuteAsync(null);
        new ProfileStore(_dir).Save([]);

        vm.Profiles.ReloadCommand.Execute(null);

        Assert.True(vm.ShowStatusConnectionButtons);
        Assert.False(vm.Profiles.ShowConnect);
    }

    [Fact]
    public async Task Reloading_profiles_does_not_disconnect()
    {
        var vm = CreateConnectableViewModel();
        await vm.ConnectCommand.ExecuteAsync(null);
        new ProfileStore(_dir).Save([]);

        vm.Profiles.ReloadCommand.Execute(null);

        Assert.Empty(vm.Profiles.Profiles);
        Assert.True(vm.IsConnected);
        await vm.ReconnectCommand.ExecuteAsync(null);
        Assert.True(vm.IsConnected);
        Assert.Equal(2, _connectCalls.Count);
    }

    private string SettingsPath => Path.Combine(_dir, "settings.json");

    [Fact]
    public async Task Connection_status_is_shown_as_name_and_tenant()
    {
        var vm = CreateConnectableViewModel();

        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.Equal("p1 (t1)", vm.ConnectedProfileTitle);
        vm.DisconnectCommand.Execute(null);
        Assert.Equal(string.Empty, vm.ConnectedProfileTitle);
    }

    private static void AssertNoBadge(NavItemViewModel item)
    {
        Assert.Equal(0, item.BadgeCount);
        Assert.Null(item.BadgeSeverity);
        Assert.False(item.HasProblem);
        Assert.False(item.ShowBadges);
    }

    private static void SelectTimeZone(MainViewModel vm, string id) =>
        vm.Preferences.SelectedTimeZone = vm.Preferences.TimeZoneChoices.Single(c => c.Id == id);

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

        return new MainViewModel(new ProfileStore(_dir), new SettingsStore(_dir), Connect, _prompt, _time, TestTimeZones.Create());
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

        public Task<IReadOnlyList<string>> GetGroupIdsWithLicenseErrorsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<EnterpriseApp>> GetEnterpriseAppsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EnterpriseApp>>([]);

        public Task<IReadOnlyList<UserRegistration>> GetUserRegistrationsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UserRegistration>>([]);

        public Task<IReadOnlyList<AppPermissionGrant>> GetGraphApplicationPermissionGrantsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AppPermissionGrant>>([]);

        public Task<IReadOnlyList<ConditionalAccessPolicy>> GetConditionalAccessPoliciesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ConditionalAccessPolicy>>([]);

        public Task<IReadOnlyList<string>> GetMissingUserIdsAsync(
            IReadOnlyCollection<string> userIds,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<DirectoryUser>> GetUsersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DirectoryUser>>([]);

        public Task<IReadOnlyList<DirectoryRoleAssignment>> GetDirectoryRoleAssignmentsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DirectoryRoleAssignment>>([]);

        public Task<IReadOnlyList<string>> GetGroupTransitiveUserIdsAsync(string groupId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<bool> GetSecurityDefaultsEnabledAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<PerUserMfaState> GetPerUserMfaStateAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(PerUserMfaState.Disabled);

        public Task<IReadOnlyDictionary<string, SingleFactorSignIns>> GetSingleFactorSignInsAsync(DateTimeOffset since, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, SingleFactorSignIns>>(new Dictionary<string, SingleFactorSignIns>());

        public Task<ConditionalAccessUsage> GetConditionalAccessUsageAsync(DateTimeOffset since, IProgress<int>? signInsRead = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConditionalAccessUsageAggregator(since, since).ToUsage());

        public Task<AuthorizationPolicySettings> GetAuthorizationPolicyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AuthorizationPolicySettings());

        public Task<CrossTenantAccessSettings> GetCrossTenantAccessDefaultsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CrossTenantAccessSettings());

        public Task<IReadOnlyList<CrossTenantAccessSettings>> GetCrossTenantAccessPartnersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CrossTenantAccessSettings>>([]);

        public Task<IReadOnlyList<GuestSignInActivity>> GetGuestSignInActivityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GuestSignInActivity>>([]);

        public Task<IReadOnlyList<DirectoryRoleDefinition>> GetDirectoryRoleDefinitionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DirectoryRoleDefinition>>([]);
    }

    private sealed class FailingReader(string? errorCode = null) : IDirectoryReader
    {
        public Task<IReadOnlyList<AppRegistration>> GetAppRegistrationsAsync(CancellationToken cancellationToken = default) =>
            throw Failure();

        public Task<IReadOnlyList<DirectoryGroup>> GetGroupsAsync(CancellationToken cancellationToken = default) =>
            throw Failure();

        public Task<IReadOnlyList<string>> GetGroupIdsWithLicenseErrorsAsync(CancellationToken cancellationToken = default) =>
            throw Failure();

        public Task<IReadOnlyList<EnterpriseApp>> GetEnterpriseAppsAsync(CancellationToken cancellationToken = default) =>
            throw Failure();

        public Task<IReadOnlyList<UserRegistration>> GetUserRegistrationsAsync(CancellationToken cancellationToken = default) =>
            throw Failure();

        public Task<IReadOnlyList<AppPermissionGrant>> GetGraphApplicationPermissionGrantsAsync(CancellationToken cancellationToken = default) =>
            throw Failure();

        public Task<IReadOnlyList<ConditionalAccessPolicy>> GetConditionalAccessPoliciesAsync(CancellationToken cancellationToken = default) =>
            throw Failure();

        public Task<IReadOnlyList<string>> GetMissingUserIdsAsync(
            IReadOnlyCollection<string> userIds,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default) =>
            throw Failure();

        public Task<IReadOnlyList<DirectoryUser>> GetUsersAsync(CancellationToken cancellationToken = default) =>
            throw Failure();

        public Task<IReadOnlyList<DirectoryRoleAssignment>> GetDirectoryRoleAssignmentsAsync(CancellationToken cancellationToken = default) =>
            throw Failure();

        public Task<IReadOnlyList<string>> GetGroupTransitiveUserIdsAsync(string groupId, CancellationToken cancellationToken = default) =>
            throw Failure();

        public Task<bool> GetSecurityDefaultsEnabledAsync(CancellationToken cancellationToken = default) =>
            throw Failure();

        public Task<PerUserMfaState> GetPerUserMfaStateAsync(string userId, CancellationToken cancellationToken = default) =>
            throw Failure();

        public Task<IReadOnlyDictionary<string, SingleFactorSignIns>> GetSingleFactorSignInsAsync(DateTimeOffset since, CancellationToken cancellationToken = default) =>
            throw Failure();

        public Task<ConditionalAccessUsage> GetConditionalAccessUsageAsync(DateTimeOffset since, IProgress<int>? signInsRead = null, CancellationToken cancellationToken = default) =>
            throw Failure();

        public Task<AuthorizationPolicySettings> GetAuthorizationPolicyAsync(CancellationToken cancellationToken = default) =>
            throw Failure();

        public Task<CrossTenantAccessSettings> GetCrossTenantAccessDefaultsAsync(CancellationToken cancellationToken = default) =>
            throw Failure();

        public Task<IReadOnlyList<CrossTenantAccessSettings>> GetCrossTenantAccessPartnersAsync(CancellationToken cancellationToken = default) =>
            throw Failure();

        public Task<IReadOnlyList<GuestSignInActivity>> GetGuestSignInActivityAsync(CancellationToken cancellationToken = default) =>
            throw Failure();

        public Task<IReadOnlyList<DirectoryRoleDefinition>> GetDirectoryRoleDefinitionsAsync(CancellationToken cancellationToken = default) =>
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

        public async Task<IReadOnlyList<string>> GetGroupIdsWithLicenseErrorsAsync(CancellationToken cancellationToken = default)
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

        public async Task<IReadOnlyList<ConditionalAccessPolicy>> GetConditionalAccessPoliciesAsync(CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return [];
        }

        public async Task<IReadOnlyList<string>> GetMissingUserIdsAsync(
            IReadOnlyCollection<string> userIds,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return [];
        }
        public async Task<IReadOnlyList<DirectoryUser>> GetUsersAsync(CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return [];
        }

        public async Task<IReadOnlyList<DirectoryRoleAssignment>> GetDirectoryRoleAssignmentsAsync(CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return [];
        }

        public async Task<IReadOnlyList<string>> GetGroupTransitiveUserIdsAsync(string groupId, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return [];
        }

        public async Task<bool> GetSecurityDefaultsEnabledAsync(CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return false;
        }

        public async Task<PerUserMfaState> GetPerUserMfaStateAsync(string userId, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return PerUserMfaState.Disabled;
        }

        public async Task<IReadOnlyDictionary<string, SingleFactorSignIns>> GetSingleFactorSignInsAsync(DateTimeOffset since, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new Dictionary<string, SingleFactorSignIns>();
        }

        public async Task<ConditionalAccessUsage> GetConditionalAccessUsageAsync(DateTimeOffset since, IProgress<int>? signInsRead = null, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new ConditionalAccessUsageAggregator(since, since).ToUsage();
        }

        public async Task<AuthorizationPolicySettings> GetAuthorizationPolicyAsync(CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new AuthorizationPolicySettings();
        }

        public async Task<CrossTenantAccessSettings> GetCrossTenantAccessDefaultsAsync(CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new CrossTenantAccessSettings();
        }

        public async Task<IReadOnlyList<CrossTenantAccessSettings>> GetCrossTenantAccessPartnersAsync(CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return [];
        }

        public async Task<IReadOnlyList<GuestSignInActivity>> GetGuestSignInActivityAsync(CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return [];
        }

        public async Task<IReadOnlyList<DirectoryRoleDefinition>> GetDirectoryRoleDefinitionsAsync(CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return [];
        }
    }
}
