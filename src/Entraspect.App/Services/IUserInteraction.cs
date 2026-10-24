using System.Threading.Tasks;

namespace Entraspect.App.Services;

/// <summary>
/// What view models need from the window: dialogs, the clipboard and the default browser. Implemented by the main
/// window and faked in tests.
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
}
