using System.ComponentModel;
using Avalonia.Controls;
using Entraspect.App.ViewModels;

namespace Entraspect.App.Views;

public partial class CheckPageView : UserControl
{
    private CheckPageViewModel? _viewModel;

    public CheckPageView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as CheckPageViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            ApplyLabels(_viewModel);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CheckPageViewModel.DueHeader) && _viewModel is not null)
        {
            ApplyLabels(_viewModel);
        }
    }

    /// <summary>
    /// DataGrid column headers aren't in the visual tree and can't bind to the page, so they are set here from the
    /// check's labels. A column whose label is null is hidden.
    /// </summary>
    private void ApplyLabels(CheckPageViewModel vm)
    {
        var labels = vm.Labels;
        SetColumn(nameof(FindingRow.ObjectDisplayName), labels.ObjectDisplayName);
        SetColumn(nameof(FindingRow.UserPrincipalName), labels.UserPrincipalName);
        SetColumn(nameof(FindingRow.ObjectId), labels.ObjectId);
        SetColumn(nameof(FindingRow.SubObjectDisplayName), labels.SubObjectDisplayName);
        SetColumn(nameof(FindingRow.SubObjectType), labels.SubObjectType);
        SetColumn(nameof(FindingRow.Value), labels.Value);
        SetColumn(nameof(FindingRow.DueDate), labels.DueDate is null ? null : vm.DueHeader);
    }

    /// <summary>Columns are found by their sort member, which is the <see cref="FindingRow"/> property they show.</summary>
    private void SetColumn(string sortMemberPath, string? header)
    {
        var column = FindingsGrid.Columns.First(c => c.SortMemberPath == sortMemberPath);
        column.Header = header;
        column.IsVisible = header is not null;
    }
}
