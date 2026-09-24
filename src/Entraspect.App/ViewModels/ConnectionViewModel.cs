using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Entraspect.App.Services;
using Entraspect.Graph;

namespace Entraspect.App.ViewModels;

public partial class ConnectionViewModel : ViewModelBase
{
    private readonly Func<ConnectionProfile, string?, Task> _onConnect;
    private readonly ProfileStore _store;

    public ConnectionViewModel(Func<ConnectionProfile, string?, Task> onConnect, ProfileStore store)
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
    public partial string CertificatePath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CertificateThumbprint { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ErrorText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ConnectionProfile? SelectedProfile { get; set; }

    public ObservableCollection<ConnectionProfile> SavedProfiles { get; }

    // The password only applies to a .pfx file; a thumbprint takes precedence over the path.
    public bool ShowPassword =>
        !string.IsNullOrWhiteSpace(CertificatePath) && string.IsNullOrWhiteSpace(CertificateThumbprint);

    // An old interactive-only profile (no certificate) still loads but cannot connect until edited.
    public bool NeedsCertificate =>
        string.IsNullOrWhiteSpace(CertificatePath) && string.IsNullOrWhiteSpace(CertificateThumbprint);

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
    private async Task Connect()
    {
        if (string.IsNullOrWhiteSpace(TenantId) || string.IsNullOrWhiteSpace(ClientId))
        {
            ErrorText = "Tenant ID and Client ID are required.";
            return;
        }

        if (NeedsCertificate)
        {
            ErrorText = "Add a certificate to use this profile.";
            return;
        }

        var profile = BuildProfile();
        var password = Password;
        Password = string.Empty;
        ErrorText = string.Empty;

        // Sign-in happens here, so bad certificates and sign-in errors surface on this view.
        try
        {
            await _onConnect(profile, password).ConfigureAwait(false);
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
        CertificatePath = !string.IsNullOrWhiteSpace(CertificatePath) ? CertificatePath.Trim() : null,
        CertificateThumbprint = !string.IsNullOrWhiteSpace(CertificateThumbprint) ? CertificateThumbprint.Trim() : null,
    };
}
