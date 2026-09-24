using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Entraspect.App.Services;
using Entraspect.Core;
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

    private async Task Connect(ConnectionProfile profile, string? password)
    {
        // No ConfigureAwait(false): CurrentView must be set on the UI thread.
        var connection = await DirectoryConnector.ConnectAsync(profile, password);
        CurrentView = new ResultsViewModel(connection, GoToConnection);
    }

    // Reuses the same connection view model so the form keeps its values.
    private void GoToConnection() => CurrentView = _connection;
}
