using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Entraspect.App.Services;
using Entraspect.Core;

namespace Entraspect.App.ViewModels;

/// <summary>
/// One check on Settings > Checks, shown as an expandable card: its name, licence pill and enable toggle, with its
/// required permissions when expanded. A check the tenant isn't licensed for has its toggle grayed out in the off
/// position; its saved on/off choice is kept, so the check comes back as it was if the tenant gets the
/// licence.
/// </summary>
public partial class CheckSettingsRow : ObservableObject
{
    private readonly SettingsState _settings;
    private readonly Action<string?> _reportError;

    // Set while the toggle is copied from reloaded settings, so it isn't saved straight back.
    private bool _syncing;

    public CheckSettingsRow(ICheck check, string categoryTitle, SettingsState settings, Action<string?> reportError)
    {
        Check = check;
        CategoryTitle = categoryTitle;
        _settings = settings;
        _reportError = reportError;
        IsEnabled = settings.IsCheckEnabled(check.Id);
        LicenceLabel = check.RequiredLicence switch
        {
            LicenceRequirement.EntraIdP1 => "P1",
            LicenceRequirement.EntraIdP2 => "P2",
            _ => null,
        };
        SetConnection(null, TenantLicence.Unknown);
    }

    public ICheck Check { get; }

    public string Title => Check.Title;

    public string CategoryTitle { get; }

    public ObservableCollection<PermissionStatusViewModel> Permissions { get; } = [];

    /// <summary>
    /// What the expanded card shows in one wrapping row: <see cref="Permissions"/>, then the licence permission for a
    /// check that needs a licence.
    /// </summary>
    public ObservableCollection<PermissionStatusViewModel> PermissionLabels { get; } = [];

    /// <summary>The licence permission label, for checks that need a licence; null otherwise.</summary>
    [ObservableProperty]
    public partial PermissionStatusViewModel? LicencePermission { get; set; }

    /// <summary>False when the connected tenant doesn't have the licence this check needs.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOn))]
    public partial bool IsLicensed { get; set; } = true;

    /// <summary>
    /// True when connected and a permission this check needs is missing, so the collapsed card shows it. Not for a check
    /// the tenant isn't licensed for, which shows no permission errors.
    /// </summary>
    [ObservableProperty]
    public partial bool HasMissingPermission { get; set; }

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

    /// <summary>"P1" or "P2", the gray pill next to the name of a check that needs that licence; null otherwise.</summary>
    public string? LicenceLabel { get; }

    /// <summary>The licence pill's hover text.</summary>
    public string LicenceToolTip => Check.RequiredLicence == LicenceRequirement.EntraIdP2
        ? "Requires an Entra ID P2 licence."
        : "Requires an Entra ID P1 or P2 licence.";

    /// <summary>The saved on/off choice.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOn))]
    public partial bool IsEnabled { get; set; }

    // Saved immediately; the next scan picks it up.
    partial void OnIsEnabledChanged(bool value)
    {
        if (!_syncing)
        {
            _reportError(_settings.SetCheckEnabled(Check.Id, value));
        }
    }

    /// <summary>Shows the saved on/off choice again, after settings.json was reloaded.</summary>
    public void SyncFromSettings()
    {
        _syncing = true;
        IsEnabled = _settings.IsCheckEnabled(Check.Id);
        _syncing = false;
    }

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
        PermissionLabels.Clear();
        foreach (var label in LicencePermission is { } licenceLabel ? Permissions.Append(licenceLabel) : Permissions)
        {
            PermissionLabels.Add(label);
        }

        IsLicensed = Licensing.IsLicensed(Check, licence);
        HasMissingPermission = IsLicensed && Permissions.Any(p => p.IsMissing);
    }
}

/// <summary>One navigation section on Settings > Checks: its title and its checks, in navigation order.</summary>
public sealed class CheckSettingsSection(string title, IReadOnlyList<CheckSettingsRow> rows)
{
    public string Title { get; } = title;

    public IReadOnlyList<CheckSettingsRow> Rows { get; } = rows;
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

        // Grouped like the navigation: sections in category order, checks in catalog order within each.
        foreach (var category in Enum.GetValues<CheckCategory>())
        {
            var title = categoryTitle(category);
            var rows = checks
                .Where(c => c.Category == category)
                .Select(c => new CheckSettingsRow(c, title, settings, e => SaveErrorText = e ?? string.Empty))
                .ToList();
            if (rows.Count == 0)
            {
                continue;
            }

            Sections.Add(new CheckSettingsSection(title, rows));
            foreach (var row in rows)
            {
                Rows.Add(row);
            }
        }

        LoadOptions();
        settings.Reloaded += (_, _) => SyncFromSettings();
    }

    /// <summary>Every check, in navigation order.</summary>
    public ObservableCollection<CheckSettingsRow> Rows { get; } = [];

    /// <summary>The checks under the navigation's section headings.</summary>
    public ObservableCollection<CheckSettingsSection> Sections { get; } = [];

    [ObservableProperty]
    public partial decimal? ExpiryWarningDays { get; set; }

    [ObservableProperty]
    public partial decimal? ExpiryCriticalDays { get; set; }

    [ObservableProperty]
    public partial decimal? NewGroupGraceDays { get; set; }

    [ObservableProperty]
    public partial decimal? ReportOnlyWarningDays { get; set; }

    [ObservableProperty]
    public partial decimal? SignInLookbackDays { get; set; }

    [ObservableProperty]
    public partial decimal? GuestInactiveDays { get; set; }

    [ObservableProperty]
    public partial decimal? GuestPendingInviteDays { get; set; }

    [ObservableProperty]
    public partial decimal? UserInactiveDays { get; set; }

    [ObservableProperty]
    public partial string OptionsErrorText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SaveErrorText { get; set; } = string.Empty;

    partial void OnExpiryWarningDaysChanged(decimal? value) => SaveOptions();

    partial void OnExpiryCriticalDaysChanged(decimal? value) => SaveOptions();

    partial void OnNewGroupGraceDaysChanged(decimal? value) => SaveOptions();

    partial void OnReportOnlyWarningDaysChanged(decimal? value) => SaveOptions();

    partial void OnSignInLookbackDaysChanged(decimal? value) => SaveOptions();

    partial void OnGuestInactiveDaysChanged(decimal? value) => SaveOptions();

    partial void OnGuestPendingInviteDaysChanged(decimal? value) => SaveOptions();

    partial void OnUserInactiveDaysChanged(decimal? value) => SaveOptions();

    /// <param name="granted">The connection's granted permissions, or null when not connected.</param>
    public void SetConnection(IReadOnlyCollection<string>? granted, TenantLicence licence)
    {
        foreach (var row in Rows)
        {
            row.SetConnection(granted, licence);
        }
    }

    /// <summary>Shows reloaded settings: every toggle and option, replacing anything not yet saved.</summary>
    private void SyncFromSettings()
    {
        foreach (var row in Rows)
        {
            row.SyncFromSettings();
        }

        LoadOptions();
        OptionsErrorText = string.Empty;
        SaveErrorText = string.Empty;
    }

    private void LoadOptions()
    {
        var options = _settings.Current.CheckOptions;
        _loading = true;
        ExpiryWarningDays = options.ExpiryWarningDays;
        ExpiryCriticalDays = options.ExpiryCriticalDays;
        NewGroupGraceDays = options.NewGroupGraceDays;
        ReportOnlyWarningDays = options.ReportOnlyWarningDays;
        SignInLookbackDays = options.SignInLookbackDays;
        GuestInactiveDays = options.GuestInactiveDays;
        GuestPendingInviteDays = options.GuestPendingInviteDays;
        UserInactiveDays = options.UserInactiveDays;
        _loading = false;
    }

    private void SaveOptions()
    {
        if (_loading)
        {
            return;
        }

        if (ExpiryWarningDays is not { } warning || ExpiryCriticalDays is not { } critical ||
            NewGroupGraceDays is not { } grace || ReportOnlyWarningDays is not { } reportOnly ||
            SignInLookbackDays is not { } lookback || GuestInactiveDays is not { } guestInactive ||
            GuestPendingInviteDays is not { } guestPendingInvite || UserInactiveDays is not { } userInactive)
        {
            OptionsErrorText = "Enter a number of days.";
            return;
        }

        var options = new CheckOptions
        {
            ExpiryWarningDays = (int)warning,
            ExpiryCriticalDays = (int)critical,
            NewGroupGraceDays = (int)grace,
            ReportOnlyWarningDays = (int)reportOnly,
            SignInLookbackDays = (int)lookback,
            GuestInactiveDays = (int)guestInactive,
            GuestPendingInviteDays = (int)guestPendingInvite,
            UserInactiveDays = (int)userInactive,
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
