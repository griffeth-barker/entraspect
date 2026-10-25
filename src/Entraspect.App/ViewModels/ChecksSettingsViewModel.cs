using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Entraspect.App.Services;
using Entraspect.Core;

namespace Entraspect.App.ViewModels;

/// <summary>One check on Settings > Checks: its enable toggle, category and required permissions.</summary>
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
        SetGrantedPermissions(null);
    }

    public ICheck Check { get; }

    public string Title => Check.Title;

    public string CategoryTitle { get; }

    public ObservableCollection<PermissionStatusViewModel> Permissions { get; } = [];

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    // Saved immediately; the next scan picks it up.
    partial void OnIsEnabledChanged(bool value) => _reportError(_settings.SetCheckEnabled(Check.Id, value));

    /// <param name="granted">The connection's granted permissions, or null when not connected.</param>
    public void SetGrantedPermissions(IReadOnlyCollection<string>? granted)
    {
        Permissions.Clear();
        foreach (var permission in Check.RequiredPermissions)
        {
            Permissions.Add(new PermissionStatusViewModel(permission, granted?.Contains(permission)));
        }
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

    public void SetGrantedPermissions(IReadOnlyCollection<string>? granted)
    {
        foreach (var row in Rows)
        {
            row.SetGrantedPermissions(granted);
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
