using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Entraspect.Core;
using Entraspect.Core.Checks;

namespace Entraspect.App.ViewModels;

public partial class ResultsViewModel : ViewModelBase
{
    private readonly IDirectoryReader _reader;
    private readonly Action _goBack;

    public ResultsViewModel(IDirectoryReader reader, Action goBack)
    {
        _reader = reader;
        _goBack = goBack;
    }

    [ObservableProperty]
    public partial ObservableCollection<Finding> Findings { get; set; } = [];

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string ErrorText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SummaryText { get; set; } =
        "Run a scan to sign in and list expiring credentials.";

    // IncludeCancelCommand generates RunScanCancelCommand, which is enabled only while a scan runs.
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task RunScanAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        ErrorText = string.Empty;
        Findings = [];

        try
        {
            var check = new AppCredentialExpiryCheck(TimeProvider.System);

            // Graph work runs on the thread pool; the await resumes on the UI thread to update bindings.
            var findings = await Task.Run(
                () => check.RunAsync(_reader, new CheckOptions(), cancellationToken),
                cancellationToken);

            Findings = new ObservableCollection<Finding>(findings);
            UpdateSummary();
        }
        catch (OperationCanceledException)
        {
            ErrorText = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            ErrorText = FormatError(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Back()
    {
        if (RunScanCommand.IsRunning)
        {
            RunScanCancelCommand.Execute(null);
        }

        _goBack();
    }

    private void UpdateSummary()
    {
        var critical = Findings.Count(f => f.Severity == Severity.Critical);
        var warning = Findings.Count(f => f.Severity == Severity.Warning);
        var info = Findings.Count(f => f.Severity == Severity.Info);
        SummaryText = $"Critical: {critical}   Warning: {warning}   Info: {info}   Total: {Findings.Count}";
    }

    private static string FormatError(Exception ex) =>
        ex is DirectoryReadException { StatusCode: 403 }
            ? $"{ex.Message}\n\nThe app registration is likely missing the Application.Read.All permission, or admin consent has not been granted."
            : ex.Message;
}
