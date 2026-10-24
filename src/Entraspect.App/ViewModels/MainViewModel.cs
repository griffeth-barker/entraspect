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
    private readonly SettingsStore _settingsStore;
    private readonly Func<ConnectionProfile, string?, CancellationToken, Task<DirectoryConnection>> _connect;
    private readonly IPasswordPrompt _passwordPrompt;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, (NavItemViewModel Item, CheckPageViewModel Page)> _checkPages = new();
    private AppSettings _settings;
    private DirectoryConnection? _connection;
    private ConnectionProfile? _connectedProfile;

    // Bumped on every scan and disconnect, so late progress reports and results from an old scan are ignored.
    private int _scanGeneration;

    public MainViewModel(IPasswordPrompt passwordPrompt)
        : this(
            new ProfileStore(),
            new SettingsStore(),
            (profile, password, ct) => DirectoryConnector.ConnectAsync(profile, password, ct),
            passwordPrompt,
            TimeProvider.System)
    {
    }

    public MainViewModel(
        ProfileStore profileStore,
        SettingsStore settingsStore,
        Func<ConnectionProfile, string?, CancellationToken, Task<DirectoryConnection>> connect,
        IPasswordPrompt passwordPrompt,
        TimeProvider timeProvider)
    {
        _settingsStore = settingsStore;
        _connect = connect;
        _passwordPrompt = passwordPrompt;
        _timeProvider = timeProvider;
        _settings = settingsStore.Load(out var settingsError);

        Checks = CheckCatalog.All(timeProvider);
        Profiles = new ConnectionViewModel(profileStore, _settings.LastUsedProfileName);
        Overview = new OverviewViewModel();

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
                var page = new CheckPageViewModel(check);
                var item = new NavItemViewModel(NavItemKind.Check, check.Title, page: page);
                section.Add(item);
                _checkPages[check.Id] = (item, page);
            }

            MenuItems.Add(section);
        }

        ConnectionProfilesItem = new NavItemViewModel(NavItemKind.SettingsPage, "Connection profiles", "Contact", Profiles);
        SettingsItem = new NavItemViewModel(NavItemKind.Settings, "Settings", "Settings")
            .Add(ConnectionProfilesItem)
            .Add(new NavItemViewModel(NavItemKind.SettingsPage, "Checks", "Library",
                new PlaceholderPageViewModel("Checks", "Enabling checks and editing check options arrives in a later step.")))
            .Add(new NavItemViewModel(NavItemKind.SettingsPage, "About", "Important",
                new PlaceholderPageViewModel("About", "entraspect is read-only: it never modifies your tenant.")));
        FooterItems.Add(SettingsItem);

        ErrorText = settingsError ?? string.Empty;
        UpdateConnectionState();
        SelectedNavItem = ConnectionProfilesItem;
    }

    public IReadOnlyList<ICheck> Checks { get; }

    public ConnectionViewModel Profiles { get; }

    public OverviewViewModel Overview { get; }

    public ObservableCollection<NavItemViewModel> MenuItems { get; } = [];

    public ObservableCollection<NavItemViewModel> FooterItems { get; } = [];

    public NavItemViewModel OverviewItem { get; }

    public NavItemViewModel SettingsItem { get; }

    public NavItemViewModel ConnectionProfilesItem { get; }

    [ObservableProperty]
    public partial NavItemViewModel? SelectedNavItem { get; set; }

    [ObservableProperty]
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
        nameof(CancelCommand))]
    public partial bool IsConnecting { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand), nameof(ReconnectCommand), nameof(ScanCommand),
        nameof(CancelCommand))]
    public partial bool IsScanning { get; set; }

    [ObservableProperty]
    public partial string ConnectedProfileName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ConnectedTenantId { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ProgressText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ErrorText { get; set; } = string.Empty;

    public bool IsNotConnected => !IsConnected;

    public bool IsBusy => IsConnecting || IsScanning;

    public string ConnectionStatusText => IsConnecting ? "Connecting…" : IsConnected ? "Connected" : "Not connected";

    /// <summary>Granted application permissions of the current connection, empty when not connected.</summary>
    public IReadOnlyList<string> GrantedPermissions => _connection?.GrantedPermissions ?? [];

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
            ErrorText = error ?? string.Empty;
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
        IsScanning = false;
        ProgressText = string.Empty;
        ErrorText = string.Empty;
        ApplyResults(null);
        UpdateConnectionState();
        SelectedNavItem = ConnectionProfilesItem;
    }

    [RelayCommand(CanExecute = nameof(CanReconnectOrScan))]
    private async Task ScanAsync(CancellationToken cancellationToken)
    {
        if (_connection is not { } connection)
        {
            return;
        }

        var generation = ++_scanGeneration;
        var enabled = Checks.Where(c => !_settings.DisabledCheckIds.Contains(c.Id)).ToList();

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
            _settings.CheckOptions,
            _timeProvider,
            progress);

        IsScanning = true;
        ErrorText = string.Empty;
        ProgressText = "Starting scan…";

        try
        {
            // Graph work runs on the thread pool; the await resumes on the UI thread to update bindings.
            var results = await Task.Run(() => runner.RunAsync(cancellationToken), cancellationToken);
            if (generation != _scanGeneration)
            {
                return;
            }

            ApplyResults(results);
            ProgressText = results.Any(r => r.Status == CheckResultStatus.Cancelled)
                ? "Scan cancelled."
                : $"Scan finished at {_timeProvider.GetUtcNow():HH:mm} UTC.";
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
                ErrorText = $"The scan stopped with an unexpected error: {ex.Message}";
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
        ErrorText = string.Empty;
        string? password = null;
        if (ConnectionViewModel.NeedsPassword(profile))
        {
            password = await _passwordPrompt.RequestPasswordAsync(profile.Name);
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
            IsScanning = false;
            ProgressText = string.Empty;
            ApplyResults(null);
            UpdateConnectionState();
            SaveLastUsedProfile(profile.Name);
            SelectedNavItem = OverviewItem;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ErrorText = "Connect cancelled.";
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private void SaveLastUsedProfile(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName) || profileName == _settings.LastUsedProfileName)
        {
            return;
        }

        _settings = new AppSettings
        {
            DisabledCheckIds = _settings.DisabledCheckIds,
            CheckOptions = _settings.CheckOptions,
            LastUsedProfileName = profileName,
        };

        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorText = $"Connected, but settings could not be saved: {ex.Message}";
        }
    }

    private void UpdateConnectionState()
    {
        IsConnected = _connection is not null;
        ConnectedProfileName = _connectedProfile?.Name ?? string.Empty;
        ConnectedTenantId = _connectedProfile?.TenantId ?? string.Empty;
        OnPropertyChanged(nameof(GrantedPermissions));

        foreach (var item in AllItems().Where(i => i.RequiresConnection))
        {
            item.IsEnabled = IsConnected;
        }
    }

    private void ApplyResults(IReadOnlyList<CheckResult>? results)
    {
        foreach (var (checkId, (item, page)) in _checkPages)
        {
            var result = results?.FirstOrDefault(r => r.CheckId == checkId);
            item.SetResult(result);
            page.SetResult(result);
        }

        Overview.SetResults(Checks, results, results is null ? null : _timeProvider.GetUtcNow());
    }

    private IEnumerable<NavItemViewModel> AllItems() =>
        MenuItems.Concat(FooterItems).SelectMany(i => i.Children.Prepend(i));

    private static string CategoryTitle(CheckCategory category) => category switch
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
        CheckCategory.ConditionalAccess => "Shield",
        _ => "AllApps",
    };
}
