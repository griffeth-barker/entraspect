using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Entraspect.App.Services;
using Entraspect.Core;

namespace Entraspect.App.ViewModels;

/// <summary>How the last scan of the current connection ended.</summary>
public enum ScanEnd
{
    None,
    Finished,
    Cancelled,
    Failed,
}

/// <summary>
/// The status bar along the bottom of the main pane, shown on every page. The left part says what is happening now:
/// connecting, the running scan with its progress, or how the last scan ended. The right part gives context: when the
/// current check last ran, or the last scan on Overview, then the connected profile's name. Status only; errors stay
/// on the page they belong to. Times go through <see cref="TimeDisplay"/>; call <see cref="Refresh"/> when the time
/// zone preference changes.
/// </summary>
public partial class StatusBarViewModel : ViewModelBase
{
    public const string NotConnectedText = "Not connected";
    public const string ConnectedText = "Connected. Select Scan to check the tenant.";
    public const string StartingScanText = "Starting scan…";
    public const string CancelledText = "Scan cancelled";
    public const string FailedText = "Scan stopped with an error";

    private readonly TimeDisplay _time;
    private string? _connectingTo;
    private string? _profileName;
    private bool _isScanning;
    private ScanProgress? _progress;
    private ScanEnd _end;
    private DateTimeOffset? _finishedAt;
    private string? _exportedTo;
    private ViewModelBase? _page;
    private DateTimeOffset? _lastScanAt;

    public StatusBarViewModel(TimeDisplay time, IRelayCommand cancelCommand)
    {
        _time = time;
        CancelCommand = cancelCommand;
        Render();
    }

    /// <summary>Cancels the running scan; the view shows it only while <see cref="IsScanning"/>.</summary>
    public IRelayCommand CancelCommand { get; }

    [ObservableProperty]
    public partial string ActivityText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsProgressVisible { get; private set; }

    [ObservableProperty]
    public partial bool IsProgressIndeterminate { get; private set; }

    /// <summary>How far the scan is, from 0 to 1: the share of checks finished.</summary>
    [ObservableProperty]
    public partial double ProgressValue { get; private set; }

    [ObservableProperty]
    public partial bool IsScanning { get; private set; }

    /// <summary>When the current page's check last ran, or the last scan on Overview; empty on other pages.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContext))]
    public partial string ContextText { get; private set; } = string.Empty;

    /// <summary>The connected profile's name, empty when not connected.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProfile))]
    public partial string ProfileName { get; private set; } = string.Empty;

    public bool HasContext => ContextText.Length > 0;

    public bool HasProfile => ProfileName.Length > 0;

    /// <summary>Shows "Connecting to …" while a connect runs; null when it has ended.</summary>
    public void SetConnecting(string? profileName)
    {
        _connectingTo = profileName;
        Render();
    }

    /// <summary>A new connection, or null after disconnecting. Either way the last scan no longer applies.</summary>
    public void SetConnection(string? profileName)
    {
        _profileName = profileName;
        _isScanning = false;
        _progress = null;
        _end = ScanEnd.None;
        _finishedAt = null;
        _exportedTo = null;
        Render();
    }

    public void ScanStarted()
    {
        _isScanning = true;
        _progress = null;
        _end = ScanEnd.None;
        _exportedTo = null;
        Render();
    }

    public void ScanProgressed(ScanProgress progress)
    {
        _progress = progress;
        Render();
    }

    /// <param name="end">How the scan ended.</param>
    /// <param name="finishedAt">When it finished, for <see cref="ScanEnd.Finished"/>.</param>
    public void ScanEnded(ScanEnd end, DateTimeOffset? finishedAt = null)
    {
        _isScanning = false;
        _progress = null;
        _end = end;
        _finishedAt = finishedAt;
        Render();
    }

    public void Exported(string fileName)
    {
        _exportedTo = fileName;
        Render();
    }

    /// <summary>The page shown, whose context the right part shows.</summary>
    public void SetPage(ViewModelBase? page)
    {
        if (_page is CheckPageViewModel oldPage)
        {
            oldPage.PropertyChanged -= OnPagePropertyChanged;
        }

        _page = page;
        if (_page is CheckPageViewModel newPage)
        {
            newPage.PropertyChanged += OnPagePropertyChanged;
        }

        Render();
    }

    /// <summary>When the last scan's results were taken, or null when there are none.</summary>
    public void SetLastScan(DateTimeOffset? scannedAt)
    {
        _lastScanAt = scannedAt;
        Render();
    }

    /// <summary>Re-shows the times after the time zone preference changes.</summary>
    public void Refresh() => Render();

    private void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CheckPageViewModel.StatusText))
        {
            Render();
        }
    }

    private void Render()
    {
        ActivityText = Activity();
        IsScanning = _isScanning;
        IsProgressVisible = _connectingTo is not null || _isScanning;

        // Indeterminate while connecting and while the scan is starting. A check's own detail doesn't move the bar.
        IsProgressIndeterminate = _connectingTo is not null || (_isScanning && _progress is null);
        ProgressValue = _isScanning && _progress is { Total: > 0 } p ? (double)(p.Position - 1) / p.Total : 0;

        ContextText = _page switch
        {
            CheckPageViewModel page => page.StatusText,
            OverviewViewModel when _lastScanAt is { } at => $"Last scan {_time.DateTime(at)}",
            _ => string.Empty,
        };
        ProfileName = _profileName ?? string.Empty;
    }

    private string Activity()
    {
        if (_connectingTo is not null)
        {
            return $"Connecting to {_connectingTo}…";
        }

        if (_isScanning)
        {
            return _progress?.Message ?? StartingScanText;
        }

        if (_profileName is null)
        {
            return NotConnectedText;
        }

        if (_exportedTo is not null)
        {
            return $"Exported to {_exportedTo}";
        }

        return _end switch
        {
            ScanEnd.Finished when _finishedAt is { } at => $"Scan finished at {_time.Time(at)}",
            ScanEnd.Cancelled => CancelledText,
            ScanEnd.Failed => FailedText,
            _ => ConnectedText,
        };
    }
}
