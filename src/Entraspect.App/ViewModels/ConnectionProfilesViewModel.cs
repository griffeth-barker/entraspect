using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Entraspect.App.Services;
using Entraspect.Graph;

namespace Entraspect.App.ViewModels;

/// <summary>A saved profile as listed on Settings > Connection profiles.</summary>
public sealed class ProfileListItem
{
    public ProfileListItem(ConnectionProfile profile)
    {
        Profile = profile;
    }

    public ConnectionProfile Profile { get; }

    public string Name => Profile.Name;

    /// <summary>The profile name with its tenant, for example "griff.systems (00000000-...)".</summary>
    public string Title => $"{Profile.Name} ({Profile.TenantId})";

    public string ClientIdText => $"Client ID {Profile.ClientId}";

    public bool NeedsCertificate => ConnectionProfilesViewModel.NeedsCertificate(Profile);
}

/// <summary>
/// Settings > Connection profiles: the saved profiles with add, edit and delete, and profiles.json to edit in a text
/// editor and reload. Connect here or in the header connects the selected profile. Profiles never contain passwords.
/// </summary>
public partial class ConnectionProfilesViewModel : ViewModelBase
{
    public const string AddCertificateText = "Add a certificate to use this profile.";

    private readonly ProfileStore _store;
    private readonly IUserInteraction _ui;

    // The name of the profile being edited, or null when adding a new one.
    private string? _editingName;

    public ConnectionProfilesViewModel(
        ProfileStore store,
        IUserInteraction ui,
        IRelayCommand connectCommand,
        string? lastUsedProfileName = null)
    {
        _store = store;
        _ui = ui;
        ConnectCommand = connectCommand;
        Profiles = new ObservableCollection<ProfileListItem>(
            store.Load(out var error).Select(p => new ProfileListItem(p)));
        ErrorText = error ?? string.Empty;
        Profiles.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoProfiles));

        // The app starts on this page with the last used profile selected.
        SelectedProfile = Profiles.FirstOrDefault(p => p.Name == lastUsedProfileName) ?? Profiles.FirstOrDefault();
    }

    public ObservableCollection<ProfileListItem> Profiles { get; }

    public IRelayCommand ConnectCommand { get; }

    public bool HasNoProfiles => Profiles.Count == 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditCommand), nameof(DeleteCommand))]
    public partial ProfileListItem? SelectedProfile { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotEditing))]
    [NotifyCanExecuteChangedFor(nameof(AddCommand), nameof(EditCommand), nameof(DeleteCommand), nameof(ReloadCommand))]
    public partial bool IsEditing { get; set; }

    public bool IsNotEditing => !IsEditing;

    [ObservableProperty]
    public partial string EditorTitle { get; set; } = string.Empty;

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
    public partial string ErrorText { get; set; } = string.Empty;

    /// <summary>A short note after profiles.json was reloaded.</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    public static bool NeedsCertificate(ConnectionProfile profile) =>
        string.IsNullOrWhiteSpace(profile.CertificatePath) && string.IsNullOrWhiteSpace(profile.CertificateThumbprint);

    /// <summary>A thumbprint takes precedence over a .pfx path, so the password is only needed without one.</summary>
    public static bool NeedsPassword(ConnectionProfile profile) =>
        string.IsNullOrWhiteSpace(profile.CertificateThumbprint) && !string.IsNullOrWhiteSpace(profile.CertificatePath);

    /// <summary>The selected profile, or null with <paramref name="error"/> set if it can't be used to connect.</summary>
    public ConnectionProfile? TryGetProfileToConnect(out string? error)
    {
        if (SelectedProfile?.Profile is not { } profile)
        {
            error = "Add or select a connection profile under Settings > Connection profiles.";
            return null;
        }

        if (NeedsCertificate(profile))
        {
            error = AddCertificateText;
            return null;
        }

        error = null;
        return profile;
    }

    /// <summary>Checks a profile before saving. Returns null if valid, otherwise what is wrong.</summary>
    public static string? Validate(ConnectionProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            return "Enter a profile name.";
        }

        if (string.IsNullOrWhiteSpace(profile.TenantId))
        {
            return "Enter the tenant ID.";
        }

        if (!Guid.TryParse(profile.ClientId, out _))
        {
            return "Enter the client ID (application ID) of the app registration, a GUID.";
        }

        var hasPath = !string.IsNullOrWhiteSpace(profile.CertificatePath);
        var hasThumbprint = !string.IsNullOrWhiteSpace(profile.CertificateThumbprint);
        return hasPath == hasThumbprint ? "Enter either a .pfx path or a certificate thumbprint." : null;
    }

    private bool CanAdd() => !IsEditing;

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        _editingName = null;
        EditorTitle = "Add profile";
        Fill(null);
        IsEditing = true;
    }

    private bool CanEditOrDelete() => SelectedProfile is not null && !IsEditing;

    [RelayCommand(CanExecute = nameof(CanEditOrDelete))]
    private void Edit()
    {
        if (SelectedProfile?.Profile is not { } profile)
        {
            return;
        }

        _editingName = profile.Name;
        EditorTitle = $"Edit '{profile.Name}'";
        Fill(profile);
        IsEditing = true;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        ErrorText = string.Empty;
    }

    [RelayCommand]
    private void Save()
    {
        var profile = new ConnectionProfile
        {
            Name = Name.Trim(),
            TenantId = TenantId.Trim(),
            ClientId = ClientId.Trim(),
            CertificatePath = !string.IsNullOrWhiteSpace(CertificatePath) ? CertificatePath.Trim() : null,
            CertificateThumbprint = !string.IsNullOrWhiteSpace(CertificateThumbprint) ? CertificateThumbprint.Trim() : null,
        };

        if (Validate(profile) is { } error)
        {
            ErrorText = error;
            return;
        }

        if (Profiles.Any(p => p.Name == profile.Name && p.Name != _editingName))
        {
            ErrorText = $"A profile named '{profile.Name}' already exists.";
            return;
        }

        var list = Profiles.Select(p => p.Profile).ToList();
        var index = list.FindIndex(p => p.Name == _editingName);
        if (_editingName is not null && index >= 0)
        {
            list[index] = profile;
        }
        else
        {
            list.Add(profile);
        }

        if (!TrySave(list))
        {
            return;
        }

        var item = new ProfileListItem(profile);
        if (_editingName is not null && index >= 0)
        {
            Profiles[index] = item;
        }
        else
        {
            Profiles.Add(item);
        }

        SelectedProfile = item;
        IsEditing = false;
        ErrorText = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanEditOrDelete))]
    private async Task DeleteAsync()
    {
        if (SelectedProfile is not { } item)
        {
            return;
        }

        var confirmed = await _ui.ConfirmAsync(
            "Delete profile",
            $"Delete the connection profile '{item.Name}'? This only removes it from entraspect; the app registration and certificate aren't changed.",
            "Delete");
        if (!confirmed)
        {
            return;
        }

        var list = Profiles.Where(p => p != item).Select(p => p.Profile).ToList();
        if (!TrySave(list))
        {
            return;
        }

        Profiles.Remove(item);
        SelectedProfile = Profiles.FirstOrDefault();
        ErrorText = string.Empty;
    }

    /// <summary>Opens profiles.json in the default app for .json files, saving it first if it doesn't exist yet.</summary>
    [RelayCommand]
    private async Task OpenFileAsync()
    {
        StatusText = string.Empty;
        ErrorText = string.Empty;
        var error = await AppFile.OpenAsync(
            _ui,
            _store.FilePath,
            () => TrySave(Profiles.Select(p => p.Profile).ToList()) ? null : ErrorText);
        ErrorText = error ?? string.Empty;
    }

    /// <summary>
    /// Reads profiles.json again and shows its profiles, validated like profiles saved from the form. If the file
    /// can't be read or a profile is invalid, shows why and keeps the profiles already loaded; the file isn't written.
    /// Doesn't disconnect: a connection whose profile was removed or renamed keeps working.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Reload()
    {
        StatusText = string.Empty;
        if (_store.Reload(out var readError) is not { } profiles)
        {
            ErrorText = readError ?? string.Empty;
            return;
        }

        foreach (var profile in profiles)
        {
            if (Validate(profile) is { } invalid)
            {
                ErrorText = string.IsNullOrWhiteSpace(profile.Name)
                    ? $"A profile in profiles.json is invalid: {invalid}"
                    : $"Profile '{profile.Name}' in profiles.json is invalid: {invalid}";
                return;
            }
        }

        if (profiles.GroupBy(p => p.Name).FirstOrDefault(g => g.Count() > 1) is { } duplicate)
        {
            ErrorText = $"profiles.json has more than one profile named '{duplicate.Key}'.";
            return;
        }

        var selectedName = SelectedProfile?.Name;
        Profiles.Clear();
        foreach (var profile in profiles)
        {
            Profiles.Add(new ProfileListItem(profile));
        }

        SelectedProfile = Profiles.FirstOrDefault(p => p.Name == selectedName) ?? Profiles.FirstOrDefault();
        ErrorText = string.Empty;
        StatusText = "Reloaded profiles.json.";
    }

    private bool TrySave(IReadOnlyList<ConnectionProfile> profiles)
    {
        try
        {
            _store.Save(profiles);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorText = $"Could not save profiles: {ex.Message}";
            return false;
        }
    }

    private void Fill(ConnectionProfile? profile)
    {
        Name = profile?.Name ?? string.Empty;
        TenantId = profile?.TenantId ?? string.Empty;
        ClientId = profile?.ClientId ?? string.Empty;
        CertificatePath = profile?.CertificatePath ?? string.Empty;
        CertificateThumbprint = profile?.CertificateThumbprint ?? string.Empty;
        ErrorText = string.Empty;
    }
}
