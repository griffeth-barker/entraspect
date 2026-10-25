using System.Threading.Tasks;

namespace Entraspect.App.Services;

/// <summary>
/// What view models need from the window: dialogs, the clipboard, the default browser, opening local files and the
/// theme. Implemented by the main window and faked in tests.
/// </summary>
public interface IUserInteraction
{
    /// <summary>Asks for a .pfx certificate password. Returns null if the user cancelled. The password is never saved.</summary>
    Task<string?> RequestPasswordAsync(string profileName);

    /// <summary>Asks the user to confirm an action. Returns true if confirmed.</summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmText);

    Task CopyTextAsync(string text);

    /// <summary>Opens a URL in the default browser. Only used for Microsoft Entra admin center pages.</summary>
    Task OpenUrlAsync(string url);

    /// <summary>
    /// Asks where to save an export, as CSV or JSON, in the platform save dialog. Returns null if the user cancelled.
    /// </summary>
    Task<ExportFile?> PickExportFileAsync(string suggestedFileName);

    /// <summary>
    /// Opens a local file (profiles.json or settings.json) in the computer's default app for its type. Returns false
    /// if no app could open it. Only hands the path to the operating system; nothing is uploaded or sent.
    /// </summary>
    Task<bool> OpenFileAsync(string path);

    /// <summary>Shows the app in light, dark or the operating system's theme, at once.</summary>
    void SetTheme(AppTheme theme);
}
