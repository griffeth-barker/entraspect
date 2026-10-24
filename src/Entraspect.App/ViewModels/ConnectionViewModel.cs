using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Entraspect.App.Services;
using Entraspect.Graph;

namespace Entraspect.App.ViewModels;

/// <summary>
/// Settings > Connection profiles. Holds the saved profiles and the profile form; Connect in the header connects the
/// profile in the form. Spec 002 step 5 replaces the form with add, edit and delete.
/// </summary>
public partial class ConnectionViewModel : ViewModelBase
{
    private readonly ProfileStore _store;

    public ConnectionViewModel(ProfileStore store, string? lastUsedProfileName = null)
    {
        _store = store;
        SavedProfiles = new ObservableCollection<ConnectionProfile>(store.Load(out var error));
        ErrorText = error ?? string.Empty;

        // The app starts on this page with the last used profile selected and loaded into the form.
        SelectedProfile = SavedProfiles.FirstOrDefault(p => p.Name == lastUsedProfileName);
    }

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TenantId { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ClientId { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsCertificate))]
    public partial string CertificatePath { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsCertificate))]
    public partial string CertificateThumbprint { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ErrorText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ConnectionProfile? SelectedProfile { get; set; }

    public ObservableCollection<ConnectionProfile> SavedProfiles { get; }

    // An old interactive-only profile (no certificate) still loads but cannot connect until edited.
    public bool NeedsCertificate =>
        string.IsNullOrWhiteSpace(CertificatePath) && string.IsNullOrWhiteSpace(CertificateThumbprint);

    /// <summary>A thumbprint takes precedence over a .pfx path, so the password is only needed without one.</summary>
    public static bool NeedsPassword(ConnectionProfile profile) =>
        string.IsNullOrWhiteSpace(profile.CertificateThumbprint) && !string.IsNullOrWhiteSpace(profile.CertificatePath);

    partial void OnSelectedProfileChanged(ConnectionProfile? value)
    {
        if (value is null)
        {
            return;
        }

        Name = value.Name;
        TenantId = value.TenantId;
        ClientId = value.ClientId;
        CertificatePath = value.CertificatePath ?? string.Empty;
        CertificateThumbprint = value.CertificateThumbprint ?? string.Empty;
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
        SelectedProfile = profile;
        ErrorText = string.Empty;
    }

    /// <summary>Builds the profile in the form for connecting, or returns null with <paramref name="error"/> set.</summary>
    public ConnectionProfile? TryGetProfileToConnect(out string? error)
    {
        if (string.IsNullOrWhiteSpace(TenantId) || string.IsNullOrWhiteSpace(ClientId))
        {
            error = "Tenant ID and Client ID are required. Fill them in under Settings > Connection profiles.";
            return null;
        }

        if (NeedsCertificate)
        {
            error = "Add a certificate to use this profile.";
            return null;
        }

        error = null;
        return BuildProfile();
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
