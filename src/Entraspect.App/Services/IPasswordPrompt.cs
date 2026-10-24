using System.Threading.Tasks;

namespace Entraspect.App.Services;

/// <summary>
/// Asks the user for a .pfx certificate password at connect time. The password is never saved.
/// </summary>
public interface IPasswordPrompt
{
    /// <summary>Returns the password, or null if the user cancelled.</summary>
    Task<string?> RequestPasswordAsync(string profileName);
}
