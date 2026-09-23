using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Entraspect.App.Services;
using Entraspect.Graph;

namespace Entraspect.App.ViewModels;

public partial class ConnectionViewModel : ViewModelBase
{
    private readonly Action<ConnectionProfile, string?> _onConnect;
    private readonly ProfileStore _store;

    public ConnectionViewModel(Action<ConnectionProfile, string?> onConnect, ProfileStore store)
    {
        _onConnect = onConnect;
        _store = store;
        SavedProfiles = new ObservableCollection<ConnectionProfile>(store.Load(out var error));
        ErrorText = error ?? string.Empty;
    }

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TenantId { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ClientId { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAppOnly), nameof(ShowPassword))]
    public partial AuthMode AuthMode { get; set; } = AuthMode.Interactive;

    [ObservableProperty]
    public partial string CertificatePath { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPassword))]
    public partial string CertificateThumbprint { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ErrorText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ConnectionProfile? SelectedProfile { get; set; }

    public ObservableCollection<ConnectionProfile> SavedProfiles { get; }

    public IEnumerable<AuthMode> AuthModes => Enum.GetValues<AuthMode>();

    public bool IsAppOnly => AuthMode == AuthMode.AppOnly;

    // The password only applies to a .pfx file; a thumbprint takes precedence over the path.
    public bool ShowPassword => IsAppOnly && string.IsNullOrWhiteSpace(CertificateThumbprint);

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
        var existing = SavedProfiles.FirstOrDefault(p => p.Name == profile.Name);
        var list = SavedProfiles.Where(p => p != existing).Append(profile).ToList();

        try
        {
            _store.Save(list);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorText = $"Could not save profiles: {ex.Message}";
            return;
        }

        if (existing is not null)
        {
            SavedProfiles.Remove(existing);
        }

        SavedProfiles.Add(profile);
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

        if (IsAppOnly &&
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

        // Certificate loading happens here, so bad paths, passwords and thumbprints surface on this view.
        try
        {
            _onConnect(profile, password);
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
        }
    }

    private ConnectionProfile BuildProfile() => new()
    {
        Name = Name.Trim(),
        TenantId = TenantId.Trim(),
        ClientId = ClientId.Trim(),
        AuthMode = AuthMode,
        CertificatePath = IsAppOnly && !string.IsNullOrWhiteSpace(CertificatePath) ? CertificatePath.Trim() : null,
        CertificateThumbprint = IsAppOnly && !string.IsNullOrWhiteSpace(CertificateThumbprint) ? CertificateThumbprint.Trim() : null,
    };
}
