using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Entraspect.Core;
using Entraspect.Core.Checks;
using Microsoft.Graph.Models.ODataErrors;

namespace Entraspect.App.ViewModels;

public partial class ResultsViewModel : ViewModelBase
{
    private readonly IDirectoryReader _reader;
    private readonly Action _goBack;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private ObservableCollection<Finding> _findings = new();

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _errorText = string.Empty;

    [ObservableProperty]
    private string _summaryText = "No scan run yet.";

    public ResultsViewModel(IDirectoryReader reader, Action goBack)
    {
        _reader = reader;
        _goBack = goBack;
    }

    [RelayCommand]
    private async Task RunScanAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorText = string.Empty;
        Findings.Clear();

        var cts = new CancellationTokenSource();
        _cts = cts;

        try
        {
            var check = new AppCredentialExpiryCheck(TimeProvider.System);
            var findings = await Task.Run(
                () => check.RunAsync(_reader, new CheckOptions(), cts.Token),
                cts.Token).ConfigureAwait(false);

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
            _cts = null;
        }
    }

    [RelayCommand]
    private void CancelScan()
    {
        _cts?.Cancel();
    }

    [RelayCommand]
    private void Back()
    {
        if (IsBusy)
        {
            _cts?.Cancel();
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

    private static string FormatError(Exception ex)
    {
        var forbidden =
            (ex is ODataError ode && ode.ResponseStatusCode == 403) ||
            ex.Message.Contains("403", StringComparison.Ordinal) ||
            ex.Message.Contains("Forbidden", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("Authorization", StringComparison.OrdinalIgnoreCase);

        var baseMessage = ex.Message;
        return forbidden
            ? $"{baseMessage}\n\nThe tenant likely lacks the 'Application.Read.All' permission (admin consent required)."
            : baseMessage;
    }
}
