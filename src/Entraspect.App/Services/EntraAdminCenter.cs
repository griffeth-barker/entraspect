namespace Entraspect.App.Services;

/// <summary>
/// Links into the Microsoft Entra admin center. entraspect only opens these pages; the admin makes any change there.
/// </summary>
public static class EntraAdminCenter
{
    public const string BaseUrl = "https://entra.microsoft.com/";

    /// <summary>
    /// The app registration's API permissions page. The portal's deep-link format isn't documented: if it stops
    /// working, the URL still starts with <see cref="BaseUrl"/> and lands in the admin center. Keep it in this one place.
    /// </summary>
    public const string ApiPermissionsUrlFormat =
        BaseUrl + "#view/Microsoft_AAD_RegisteredApps/ApplicationMenuBlade/~/CallAnAPI/appId/{0}/isMSAApp~/false";

    public static string ApiPermissionsUrl(string clientId) =>
        string.Format(System.Globalization.CultureInfo.InvariantCulture, ApiPermissionsUrlFormat,
            Uri.EscapeDataString(clientId));
}
