using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Entraspect.App.Services;
using Entraspect.App.ViewModels;
using Entraspect.Core;
using Entraspect.Graph;

namespace Entraspect.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly ProfileStore _profileStore = new();

    [ObservableProperty]
    private ViewModelBase _currentView;

    public MainViewModel()
    {
        _currentView = new ConnectionViewModel(Connect, _profileStore);
    }

    private void Connect(ConnectionProfile profile, string? password)
    {
        var reader = ReaderFactory.Create(profile, password);
        CurrentView = new ResultsViewModel(reader, GoToConnection);
    }

    private void GoToConnection()
    {
        CurrentView = new ConnectionViewModel(Connect, _profileStore);
    }
}
