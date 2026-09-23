using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Entraspect.App.Services;
using Entraspect.Graph;

namespace Entraspect.App.ViewModels;

public partial class ConnectionViewModel : ViewModelBase
{
    private readonly Action<ConnectionProfile, string?> _onConnect;
    private readonly ProfileStore _store;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _tenantId = string.Empty;

    [ObservableProperty]
    private string _clientId = string.Empty;

    [ObservableProperty]
    private AuthMode _authMode = AuthMode.Interactive;

    [ObservableProperty]
    private string _certificatePath = string.Empty;

    [ObservableProperty]
    private string _certificateThumbprint = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _errorText = string.Empty;

    [ObservableProperty]
    private bool _isAppOnly;

    [ObservableProperty]
    private ConnectionProfile? _selectedProfile;

    [ObservableProperty]
    private ObservableCollection<ConnectionProfile> _savedProfiles = new();

    partial void OnAuthModeChanged(AuthMode value) => IsAppOnly = value == AuthMode.AppOnly;

    public ConnectionViewModel(Action<ConnectionProfile, string?> onConnect, ProfileStore store)
    {
        _onConnect = onConnect;
        _store = store;
        SavedProfiles = new ObservableCollection<ConnectionProfile>(store.Load());
    }

    public IEnumerable<AuthMode> AuthModes => System.Enum.GetValues<AuthMode>();

    [RelayCommand]
    private void LoadProfile(ConnectionProfile? profile)
    {
        if (profile is null)
        {
            return;
        }

        Name = profile.Name;
        TenantId = profile.TenantId;
        ClientId = profile.ClientId;
        AuthMode = profile.AuthMode;
        CertificatePath = profile.CertificatePath ?? string.Empty;
        CertificateThumbprint = profile.CertificateThumbprint ?? string.Empty;
        Password = string.Empty;
        ErrorText = string.Empty;
    }

    [RelayCommand]
    private void SaveProfile()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorText = "Profile name is required to save.";
            return;
        }

        var profile = BuildProfile();
        var list = SavedProfiles.Where(p => p.Name != profile.Name).ToList();
        list.Add(profile);
        SavedProfiles = new ObservableCollection<ConnectionProfile>(list);
        _store.Save(list);
        ErrorText = string.Empty;
    }

    [RelayCommand]
    private void Connect()
    {
        if (string.IsNullOrWhiteSpace(TenantId) || string.IsNullOrWhiteSpace(ClientId))
        {
            ErrorText = "Tenant ID and Client ID are required.";
            return;
        }

        if (AuthMode == AuthMode.AppOnly &&
            string.IsNullOrWhiteSpace(CertificatePath) &&
            string.IsNullOrWhiteSpace(CertificateThumbprint))
        {
            ErrorText = "App-only authentication requires a certificate path or thumbprint.";
            return;
        }

        var profile = BuildProfile();
        var password = Password;
        Password = string.Empty;
        ErrorText = string.Empty;
        _onConnect(profile, password);
    }

    private ConnectionProfile BuildProfile() => new()
    {
        Name = Name.Trim(),
        TenantId = TenantId.Trim(),
        ClientId = ClientId.Trim(),
        AuthMode = AuthMode,
        CertificatePath = string.IsNullOrWhiteSpace(CertificatePath) ? null : CertificatePath.Trim(),
        CertificateThumbprint = string.IsNullOrWhiteSpace(CertificateThumbprint) ? null : CertificateThumbprint.Trim(),
    };
}
