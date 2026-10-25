using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Entraspect.App.Services;
using Entraspect.Core;

namespace Entraspect.App.ViewModels;

/// <summary>
/// One check on Settings > Checks: its enable toggle, category and required permissions. A check the tenant isn't
/// licensed for has its toggle grayed out in the off position with a note; its saved on/off choice is kept, so the
/// check comes back as it was if the tenant gets the licence.
/// </summary>
public partial class CheckSettingsRow : ObservableObject
{
    private readonly SettingsState _settings;
    private readonly Action<string?> _reportError;

    public CheckSettingsRow(ICheck check, string categoryTitle, SettingsState settings, Action<string?> reportError)
    {
        Check = check;
        CategoryTitle = categoryTitle;
        _settings = settings;
        _reportError = reportError;
        IsEnabled = settings.IsCheckEnabled(check.Id);
        LicenceNote = Licensing.Needs(check.RequiredLicence);
        SetConnection(null, TenantLicence.Unknown);
    }

    public ICheck Check { get; }

    public string Title => Check.Title;

    public string CategoryTitle { get; }

    public ObservableCollection<PermissionStatusViewModel> Permissions { get; } = [];

    /// <summary>The licence permission label, for checks that need a licence; null otherwise.</summary>
    [ObservableProperty]
    public partial PermissionStatusViewModel? LicencePermission { get; set; }

    /// <summary>False when the connected tenant doesn't have the licence this check needs.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOn))]
    public partial bool IsLicensed { get; set; } = true;

    /// <summary>
    /// What the toggle shows: off for an unlicensed check, whatever is saved. Changing it saves
    /// <see cref="IsEnabled"/>; an unlicensed check's toggle is grayed out, so it can't be changed.
    /// </summary>
    public bool IsOn
    {
        get => IsLicensed && IsEnabled;
        set
        {
            if (IsLicensed)
            {
                IsEnabled = value;
            }
        }
    }

    public string LicenceNote { get; }

    /// <summary>The saved on/off choice.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOn))]
    public partial bool IsEnabled { get; set; }

    // Saved immediately; the next scan picks it up.
    partial void OnIsEnabledChanged(bool value) => _reportError(_settings.SetCheckEnabled(Check.Id, value));

    /// <param name="granted">The connection's granted permissions, or null when not connected.</param>
    /// <param name="licence">The connected tenant's licence; <see cref="TenantLicence.Unknown"/> when not connected.</param>
    public void SetConnection(IReadOnlyCollection<string>? granted, TenantLicence licence)
    {
        Permissions.Clear();
        foreach (var permission in Check.RequiredPermissions)
        {
            Permissions.Add(new PermissionStatusViewModel(permission, granted?.Contains(permission)));
        }

        LicencePermission = Check.RequiredLicence == LicenceRequirement.None
            ? null
            : new PermissionStatusViewModel(
                CheckCatalog.LicencePermission,
                granted?.Contains(CheckCatalog.LicencePermission),
                isOptional: true);
        IsLicensed = Licensing.IsLicensed(Check, licence);
    }
}

/// <summary>
/// Settings > Checks: turn checks on or off and edit <see cref="CheckOptions"/>. Available before connecting; changes
/// are saved immediately. Invalid options are shown and not saved.
/// </summary>
public partial class ChecksSettingsViewModel : ViewModelBase
{
    private readonly SettingsState _settings;
    private bool _loading;

    public ChecksSettingsViewModel(
        IReadOnlyList<ICheck> checks,
        Func<CheckCategory, string> categoryTitle,
        SettingsState settings)
    {
        _settings = settings;
        foreach (var check in checks)
        {
            Rows.Add(new CheckSettingsRow(check, categoryTitle(check.Category), settings, e => SaveErrorText = e ?? string.Empty));
        }

        _loading = true;
        ExpiryWarningDays = settings.Current.CheckOptions.ExpiryWarningDays;
        ExpiryCriticalDays = settings.Current.CheckOptions.ExpiryCriticalDays;
        NewGroupGraceDays = settings.Current.CheckOptions.NewGroupGraceDays;
        _loading = false;
    }

    public ObservableCollection<CheckSettingsRow> Rows { get; } = [];

    [ObservableProperty]
    public partial decimal? ExpiryWarningDays { get; set; }

    [ObservableProperty]
    public partial decimal? ExpiryCriticalDays { get; set; }

    [ObservableProperty]
    public partial decimal? NewGroupGraceDays { get; set; }

    [ObservableProperty]
    public partial string OptionsErrorText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SaveErrorText { get; set; } = string.Empty;

    partial void OnExpiryWarningDaysChanged(decimal? value) => SaveOptions();

    partial void OnExpiryCriticalDaysChanged(decimal? value) => SaveOptions();

    partial void OnNewGroupGraceDaysChanged(decimal? value) => SaveOptions();

    /// <param name="granted">The connection's granted permissions, or null when not connected.</param>
    public void SetConnection(IReadOnlyCollection<string>? granted, TenantLicence licence)
    {
        foreach (var row in Rows)
        {
            row.SetConnection(granted, licence);
        }
    }

    private void SaveOptions()
    {
        if (_loading)
        {
            return;
        }

        if (ExpiryWarningDays is not { } warning || ExpiryCriticalDays is not { } critical ||
            NewGroupGraceDays is not { } grace)
        {
            OptionsErrorText = "Enter a number of days.";
            return;
        }

        var options = new CheckOptions
        {
            ExpiryWarningDays = (int)warning,
            ExpiryCriticalDays = (int)critical,
            NewGroupGraceDays = (int)grace,
        };

        if (CheckOptions.Validate(options) is { } error)
        {
            OptionsErrorText = $"{error} Not saved.";
            return;
        }

        OptionsErrorText = string.Empty;
        SaveErrorText = _settings.SetCheckOptions(options) ?? string.Empty;
    }
}
