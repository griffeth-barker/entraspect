using CommunityToolkit.Mvvm.ComponentModel;
using Entraspect.App.Services;
using Entraspect.Graph;

namespace Entraspect.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly ConnectionViewModel _connection;

    public MainViewModel()
    {
        _connection = new ConnectionViewModel(Connect, new ProfileStore());
        CurrentView = _connection;
    }

    [ObservableProperty]
    public partial ViewModelBase CurrentView { get; set; }

    private void Connect(ConnectionProfile profile, string? password)
    {
        var reader = DirectoryReaderFactory.Create(profile, password);
        CurrentView = new ResultsViewModel(reader, GoToConnection);
    }

    // Reuses the same connection view model so the form keeps its values.
    private void GoToConnection() => CurrentView = _connection;
}
