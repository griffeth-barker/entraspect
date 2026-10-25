using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Entraspect.App.Services;
using Entraspect.Core;
using Entraspect.Graph;

namespace Entraspect.App.ViewModels;

/// <summary>
/// The shell: navigation items, the selected page, the connection and the scan. The app starts on
/// Settings > Connection profiles; Overview and the check pages unlock once connected.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly SettingsState _settings;
    private readonly Func<ConnectionProfile, string?, CancellationToken, Task<DirectoryConnection>> _connect;
    private readonly IUserInteraction _ui;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, (NavItemViewModel Item, CheckPageViewModel Page)> _checkPages = new();
    private DirectoryConnection? _connection;
    private ConnectionProfile? _connectedProfile;
    private DateTimeOffset? _connectedAt;
    private IReadOnlyList<CheckResult>? _lastResults;
    private DateTimeOffset? _lastScanAt;

    // Bumped on every scan and disconnect, so late progress reports and results from an old scan are ignored.
    private int _scanGeneration;

    public MainViewModel(IUserInteraction ui)
        : this(
            new ProfileStore(),
            new SettingsStore(),
            (profile, password, ct) => DirectoryConnector.ConnectAsync(profile, password, ct),
            ui,
            TimeProvider.System)
    {
    }

    public MainViewModel(
        ProfileStore profileStore,
        SettingsStore settingsStore,
        Func<ConnectionProfile, string?, CancellationToken, Task<DirectoryConnection>> connect,
        IUserInteraction ui,
        TimeProvider timeProvider,
        TimeZones? timeZones = null)
    {
        _connect = connect;
        _ui = ui;
        _timeProvider = timeProvider;
        _settings = new SettingsState(settingsStore);
        Time = new TimeDisplay(_settings, timeZones);

        Checks = CheckCatalog.All(timeProvider);
        ExcessPermissions = new ExcessPermissionsViewModel();
        Profiles = new ConnectionProfilesViewModel(profileStore, ui, ConnectCommand, _settings.Current.LastUsedProfileName);
        Overview = new OverviewViewModel(NewBanner(), ExcessPermissions, item => SelectedNavItem = item);
        ChecksSettings = new ChecksSettingsViewModel(Checks, CategoryTitle, _settings);
        About = new AboutViewModel(ExcessPermissions, profileStore.FolderPath, _settings, Time, ui);
        Preferences = new PreferencesViewModel(_settings, Time, ui);

        OverviewItem = new NavItemViewModel(NavItemKind.Overview, "Overview", "Home", Overview);
        MenuItems.Add(OverviewItem);

        foreach (var category in Enum.GetValues<CheckCategory>())
        {
            var checks = Checks.Where(c => c.Category == category).ToList();
            if (checks.Count == 0)
            {
                continue;
            }

            var section = new NavItemViewModel(NavItemKind.Section, CategoryTitle(category), CategoryIcon(category));
            foreach (var check in checks)
            {
                var page = new CheckPageViewModel(check, NewBanner(), Time);
                var item = new NavItemViewModel(NavItemKind.Check, check.Title, page: page);
                section.Add(item);
                _checkPages[check.Id] = (item, page);
            }

            MenuItems.Add(section);
        }

        ConnectionProfilesItem = new NavItemViewModel(NavItemKind.SettingsPage, "Connection profiles", "Contact", Profiles);
        SettingsItem = new NavItemViewModel(NavItemKind.Settings, "Settings", "Settings")
            .Add(ConnectionProfilesItem)
            .Add(ChecksSettingsItem = new NavItemViewModel(NavItemKind.SettingsPage, "Checks", "Library", ChecksSettings))
            .Add(new NavItemViewModel(NavItemKind.SettingsPage, "Preferences", "Clock", Preferences))
            .Add(new NavItemViewModel(NavItemKind.SettingsPage, "About", "Important", About));
        FooterItems.Add(SettingsItem);

        // Turning a check on or off changes which missing permissions the banners list; the time zone preference
        // changes how every date is shown.
        _settings.Changed += (_, _) =>
        {
            RefreshPermissions();
            RefreshTimes();
        };

        ConnectionErrorText = _settings.LoadError ?? string.Empty;
        UpdateConnectionState();
        SelectedNavItem = ConnectionProfilesItem;
    }

    public IReadOnlyList<ICheck> Checks { get; }

    public ConnectionProfilesViewModel Profiles { get; }

    public OverviewViewModel Overview { get; }

    public ChecksSettingsViewModel ChecksSettings { get; }

    public AboutViewModel About { get; }

    public PreferencesViewModel Preferences { get; }

    public TimeDisplay Time { get; }

    public ExcessPermissionsViewModel ExcessPermissions { get; }

    public ObservableCollection<NavItemViewModel> MenuItems { get; } = [];

    public ObservableCollection<NavItemViewModel> FooterItems { get; } = [];

    public NavItemViewModel OverviewItem { get; }

    public NavItemViewModel SettingsItem { get; }

    public NavItemViewModel ConnectionProfilesItem { get; }

    public NavItemViewModel ChecksSettingsItem { get; }

    [ObservableProperty]
    public partial NavItemViewModel? SelectedNavItem { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExportToolTip))]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand), nameof(ExportCommand))]
    public partial ViewModelBase? CurrentPage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionStatusText), nameof(IsNotConnected))]
    [NotifyCanExecuteChangedFor(
        nameof(ConnectCommand), nameof(ReconnectCommand), nameof(DisconnectCommand), nameof(ScanCommand))]
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionStatusText), nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(
        nameof(ConnectCommand), nameof(ReconnectCommand), nameof(DisconnectCommand), nameof(ScanCommand),
        nameof(CancelCommand), nameof(ExportCommand))]
    public partial bool IsConnecting { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand), nameof(ReconnectCommand), nameof(ScanCommand),
        nameof(CancelCommand), nameof(ExportCommand))]
    public partial bool IsScanning { get; set; }

    [ObservableProperty]
    public partial string ProgressText { get; set; } = string.Empty;

    /// <summary>Why connecting failed, shown on Settings > Connection profiles.</summary>
    [ObservableProperty]
    public partial string ConnectionErrorText { get; set; } = string.Empty;

    /// <summary>An unexpected scan error, shown in the scan bar on Overview and the check pages.</summary>
    [ObservableProperty]
    public partial string ScanErrorText { get; set; } = string.Empty;

    /// <summary>Why the last export couldn't be written, shown in the scan bar.</summary>
    [ObservableProperty]
    public partial string ExportErrorText { get; set; } = string.Empty;

    /// <summary>What Export saves on the current page.</summary>
    public string ExportToolTip => CurrentPage is CheckPageViewModel
        ? "Export the findings shown as CSV or JSON"
        : "Export every finding from the last scan as CSV or JSON";

    /// <summary>The connected profile as "name (tenant ID)", empty when not connected.</summary>
    [ObservableProperty]
    public partial string ConnectedProfileTitle { get; set; } = string.Empty;

    public bool IsNotConnected => !IsConnected;

    public bool IsBusy => IsConnecting || IsScanning;

    public string ConnectionStatusText => IsConnecting ? "Connecting…" : IsConnected ? "Connected" : "Not connected";

    /// <summary>Granted application permissions of the current connection, empty when not connected.</summary>
    public IReadOnlyList<string> GrantedPermissions => _connection?.GrantedPermissions ?? [];

    /// <summary>The connected tenant's licence, <see cref="TenantLicence.Unknown"/> when not connected or not read.</summary>
    public TenantLicence TenantLicence => _connection?.TenantLicence ?? TenantLicence.Unknown;

    partial void OnSelectedNavItemChanged(NavItemViewModel? oldValue, NavItemViewModel? newValue)
    {
        // Sections only expand; a disabled page can't be opened. Either way keep the previous selection.
        if (newValue is null || !newValue.IsPage || !newValue.IsEnabled)
        {
            SelectedNavItem = oldValue is { IsPage: true, IsEnabled: true } ? oldValue : ConnectionProfilesItem;
            return;
        }

        if (newValue.Parent is { } parent)
        {
            parent.IsExpanded = true;
        }

        CurrentPage = newValue.Page;
    }

    private bool CanConnect() => !IsConnected && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        var profile = Profiles.TryGetProfileToConnect(out var error);
        if (profile is null)
        {
            ConnectionErrorText = error ?? string.Empty;
            SelectedNavItem = ConnectionProfilesItem;
            return;
        }

        await ConnectProfileAsync(profile, cancellationToken);
    }

    private bool CanReconnectOrScan() => IsConnected && !IsBusy;

    /// <summary>Gets a new credential and token, so permissions granted since the last connect are picked up.</summary>
    [RelayCommand(CanExecute = nameof(CanReconnectOrScan))]
    private async Task ReconnectAsync(CancellationToken cancellationToken)
    {
        if (_connectedProfile is { } profile)
        {
            await ConnectProfileAsync(profile, cancellationToken);
        }
    }

    private bool CanDisconnect() => IsConnected && !IsConnecting;

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private void Disconnect()
    {
        CancelRunningWork();
        _scanGeneration++;
        _connection = null;
        _connectedProfile = null;
        _connectedAt = null;
        IsScanning = false;
        ProgressText = string.Empty;
        ScanErrorText = string.Empty;
        ConnectionErrorText = string.Empty;
        ApplyResults(null, null);
        UpdateConnectionState();
        SelectedNavItem = ConnectionProfilesItem;
    }

    // Not on the page of a check the tenant isn't licensed for: the scan there would do nothing for it.
    private bool CanScan() => CanReconnectOrScan() && CurrentPage is not CheckPageViewModel { IsLicensed: false };

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync(CancellationToken cancellationToken)
    {
        if (_connection is not { } connection)
        {
            return;
        }

        var generation = ++_scanGeneration;
        var enabled = EnabledChecks();

        // Progress<T> captures the UI synchronization context, so the handler runs on the UI thread.
        var progress = new Progress<ScanProgress>(p =>
        {
            if (generation == _scanGeneration && IsScanning)
            {
                ProgressText = p.Message;
            }
        });

        var runner = new ScanRunner(
            new CachingDirectoryReader(connection.Reader),
            enabled,
            connection.GrantedPermissions,
            _settings.Current.CheckOptions,
            _timeProvider,
            progress,
            connection.TenantLicence);

        IsScanning = true;
        ScanErrorText = string.Empty;
        ExportErrorText = string.Empty;
        ProgressText = "Starting scan…";

        try
        {
            // Graph work runs on the thread pool; the await resumes on the UI thread to update bindings.
            var results = await Task.Run(() => runner.RunAsync(cancellationToken), cancellationToken);
            if (generation != _scanGeneration)
            {
                return;
            }

            ApplyResults(results, _timeProvider.GetUtcNow());
            ProgressText = results.Any(r => r.Status == CheckResultStatus.Cancelled)
                ? "Scan cancelled."
                : $"Scan finished at {Time.Time(_timeProvider.GetUtcNow())}.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (generation == _scanGeneration)
            {
                ProgressText = "Scan cancelled.";
            }
        }
        catch (Exception ex)
        {
            // ScanRunner turns read failures into check results, so this is an unexpected error in a check.
            if (generation == _scanGeneration)
            {
                ScanErrorText = $"The scan stopped with an unexpected error: {ex.Message}";
                ProgressText = string.Empty;
            }
        }
        finally
        {
            if (generation == _scanGeneration)
            {
                IsScanning = false;
            }
        }
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel() => CancelRunningWork();

    // Only once a scan has finished, and on a check page only if the check ran in it.
    private bool CanExport() => !IsBusy && _lastResults is not null && CurrentPage switch
    {
        OverviewViewModel => true,
        CheckPageViewModel page => page.Result is not null,
        _ => false,
    };

    /// <summary>
    /// Saves results to a CSV or JSON file the user picks: on Overview every finding from the last scan, on a check
    /// page the findings it shows after search and filters. Only writes that local file.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        if (ExportReport() is not { } report)
        {
            return;
        }

        ExportErrorText = string.Empty;
        var file = await _ui.PickExportFileAsync(
            ExportFileName.For(report.ProfileName, Time.FileStamp(report.ScannedAt), ExportFormat.Csv));
        if (file is null)
        {
            return;
        }

        try
        {
            await using var stream = await file.OpenWriteAsync();
            await ScanExport.WriteAsync(stream, report, file.Format);
            ProgressText = $"Exported to {file.Name}.";
        }
        catch (Exception ex)
        {
            ExportErrorText = $"Couldn't export to {file.Name}: {ex.Message}";
        }
    }

    private ScanReport? ExportReport()
    {
        if (_lastResults is not { } results || _lastScanAt is not { } scannedAt || _connectedProfile is not { } profile)
        {
            return null;
        }

        var report = ScanReport.Create(scannedAt, profile.Name, profile.TenantId, About.Version, Checks, results);
        return CurrentPage switch
        {
            OverviewViewModel => report,
            CheckPageViewModel { Result: { } result } page => report with
            {
                Checks = [ScanReportCheck.From(page.Check, result, page.Findings.Select(r => r.Finding).ToList())],
            },
            _ => null,
        };
    }

    /// <summary>Called when the window closes, so a running scan or connect does not keep going.</summary>
    public void Shutdown() => CancelRunningWork();

    private void CancelRunningWork()
    {
        ConnectCommand.Cancel();
        ReconnectCommand.Cancel();
        ScanCommand.Cancel();
    }

    private async Task ConnectProfileAsync(ConnectionProfile profile, CancellationToken cancellationToken)
    {
        ConnectionErrorText = string.Empty;
        string? password = null;
        if (ConnectionProfilesViewModel.NeedsPassword(profile))
        {
            password = await _ui.RequestPasswordAsync(profile.Name);
            if (password is null)
            {
                return;
            }
        }

        IsConnecting = true;
        try
        {
            // Loading the certificate and getting the token both stay off the UI thread.
            var connection = await Task.Run(() => _connect(profile, password, cancellationToken), cancellationToken);

            // A reconnect replaces the old connection; results from it no longer apply to the new permissions.
            _scanGeneration++;
            _connection = connection;
            _connectedProfile = profile;
            _connectedAt = _timeProvider.GetUtcNow();
            IsScanning = false;
            ProgressText = string.Empty;
            ScanErrorText = string.Empty;
            ApplyResults(null, null);
            UpdateConnectionState();
            if (_settings.SetLastUsedProfile(profile.Name) is { } saveError)
            {
                ConnectionErrorText = $"Connected, but {saveError}";
            }

            SelectedNavItem = OverviewItem;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ConnectionErrorText = "Connect cancelled.";
        }
        catch (Exception ex)
        {
            ConnectionErrorText = ex.Message;
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private void UpdateConnectionState()
    {
        IsConnected = _connection is not null;
        Overview.SetConnection(_connectedProfile, _connectedAt is { } at ? Time.DateTime(at) : null);
        ConnectedProfileTitle = _connectedProfile is { } p ? $"{p.Name} ({p.TenantId})" : string.Empty;
        OnPropertyChanged(nameof(GrantedPermissions));
        OnPropertyChanged(nameof(TenantLicence));

        RefreshPermissions();
    }

    /// <summary>
    /// Updates everything that depends on the granted permissions, the tenant licence and the enabled checks: the
    /// missing-permission banners, the granted or missing labels, the unlicensed checks, and the excess permissions
    /// banners.
    /// </summary>
    private void RefreshPermissions()
    {
        IReadOnlyCollection<string>? granted = _connection is null
            ? null
            : new HashSet<string>(_connection.GrantedPermissions, StringComparer.Ordinal);
        var licence = TenantLicence;

        foreach (var (checkId, (_, page)) in _checkPages)
        {
            page.IsDisabled = !_settings.IsCheckEnabled(checkId);
            page.SetConnection(granted, licence);
        }

        // Overview and the check pages need a connection; a check page also needs the tenant licensed for its check.
        foreach (var item in AllItems().Where(i => i.RequiresConnection))
        {
            var unlicensed = item.Page is CheckPageViewModel { IsLicensed: false } checkPage ? checkPage : null;
            item.IsEnabled = IsConnected && unlicensed is null;
            item.DisabledReason = IsConnected && unlicensed is not null
                ? Licensing.Needs(unlicensed.Check.RequiredLicence)
                : null;
        }

        ScanCommand.NotifyCanExecuteChanged();

        ChecksSettings.SetConnection(granted, licence);
        About.IsConnected = granted is not null;

        if (granted is null)
        {
            ChecksSettingsItem.SetWarning(false);
            Overview.Banner.Hide();
            ExcessPermissions.SetReview(null);
            return;
        }

        // Missing permissions only count for enabled checks; unused ones are judged against every check.
        var missing = PermissionReview.Review(EnabledChecks(), granted, licence).MissingPermissions;
        Overview.Banner.ShowForOverview(missing);
        ChecksSettingsItem.SetWarning(missing.Count > 0);
        ExcessPermissions.SetReview(PermissionReview.Review(Checks, granted, licence));
    }

    private List<ICheck> EnabledChecks() => Checks.Where(c => _settings.IsCheckEnabled(c.Id)).ToList();

    private PermissionBannerViewModel NewBanner() => new(OpenAdminCenterAsync, _ui.CopyTextAsync, ReconnectCommand);

    /// <summary>Opens the connected app registration's API permissions page, where the admin adds permissions.</summary>
    private Task OpenAdminCenterAsync() => _connection is { } connection
        ? _ui.OpenUrlAsync(EntraAdminCenter.ApiPermissionsUrl(connection.ClientId))
        : _ui.OpenUrlAsync(EntraAdminCenter.BaseUrl);

    private void ApplyResults(IReadOnlyList<CheckResult>? results, DateTimeOffset? scannedAt)
    {
        _lastResults = results;
        _lastScanAt = scannedAt;
        foreach (var (checkId, (item, page)) in _checkPages)
        {
            var result = results?.FirstOrDefault(r => r.CheckId == checkId);
            item.SetResult(result);
            page.SetResult(result);
        }

        Overview.SetResults(
            MenuItems.Where(i => i.Kind == NavItemKind.Section),
            results,
            scannedAt is { } at ? Time.DateTime(at) : null);
        ExportCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Re-shows every date and time after the time zone preference changes.</summary>
    private void RefreshTimes()
    {
        ApplyResults(_lastResults, _lastScanAt);
        Overview.SetConnection(_connectedProfile, _connectedAt is { } at ? Time.DateTime(at) : null);
    }

    private IEnumerable<NavItemViewModel> AllItems() =>
        MenuItems.Concat(FooterItems).SelectMany(i => i.Children.Prepend(i));

    public static string CategoryTitle(CheckCategory category) => category switch
    {
        CheckCategory.Users => "Users",
        CheckCategory.Groups => "Groups",
        CheckCategory.AppRegistrations => "App registrations",
        CheckCategory.EnterpriseApps => "Enterprise apps",
        CheckCategory.ConditionalAccess => "Conditional Access",
        _ => category.ToString(),
    };

    private static string CategoryIcon(CheckCategory category) => category switch
    {
        CheckCategory.Users => "Contact",
        CheckCategory.Groups => "People",
        CheckCategory.AppRegistrations => "AllApps",
        CheckCategory.EnterpriseApps => "Globe",
        CheckCategory.ConditionalAccess => "Permissions",
        _ => "AllApps",
    };
}
