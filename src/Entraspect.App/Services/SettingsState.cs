using Entraspect.Core;

namespace Entraspect.App.Services;

/// <summary>
/// The current <see cref="AppSettings"/>, shared by the shell and the settings pages. Every change is saved
/// immediately and raises <see cref="Changed"/>.
/// </summary>
public sealed class SettingsState
{
    private readonly SettingsStore _store;

    public SettingsState(SettingsStore store)
    {
        _store = store;
        Current = store.Load(out var error);
        LoadError = error;
    }

    public AppSettings Current { get; private set; }

    /// <summary>Why settings.json could not be read at startup, or null.</summary>
    public string? LoadError { get; }

    public event EventHandler? Changed;

    public bool IsCheckEnabled(string checkId) => !Current.DisabledCheckIds.Contains(checkId);

    /// <summary>Saves new settings. Returns null on success, otherwise why they could not be saved.</summary>
    public string? Update(Func<AppSettings, AppSettings> change)
    {
        Current = change(Current);
        Changed?.Invoke(this, EventArgs.Empty);

        try
        {
            _store.Save(Current);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Settings could not be saved: {ex.Message}";
        }
    }

    public string? SetCheckEnabled(string checkId, bool enabled) => Update(s => s with
    {
        DisabledCheckIds = enabled
            ? s.DisabledCheckIds.Where(id => id != checkId).ToList()
            : s.DisabledCheckIds.Append(checkId).Distinct().ToList(),
    });

    public string? SetCheckOptions(CheckOptions options) => Update(s => s with { CheckOptions = options });

    /// <param name="timeZone"><see cref="AppSettings.UtcTimeZone"/>, <see cref="AppSettings.LocalTimeZone"/> or a zone ID.</param>
    public string? SetTimeZone(string timeZone) => timeZone == Current.TimeZone
        ? null
        : Update(s => s with { TimeZone = timeZone });

    public string? SetLastUsedProfile(string profileName) => profileName == Current.LastUsedProfileName
        ? null
        : Update(s => s with { LastUsedProfileName = profileName });
}
