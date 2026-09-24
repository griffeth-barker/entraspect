using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Entraspect.Core;
using Microsoft.Graph;

namespace Entraspect.Graph;

/// <summary>
/// Connects to a tenant using app-only certificate auth and returns a Core-owned <see cref="DirectoryConnection"/>.
/// The token is requested up front, so sign-in errors appear at connect time. Graph SDK and Azure.Identity types
/// never leave this project.
/// </summary>
public static class DirectoryConnector
{
    /// <param name="certificatePassword">Password for a .pfx file. Used once to load the certificate and never stored.</param>
    public static async Task<DirectoryConnection> ConnectAsync(
        ConnectionProfile profile,
        string? certificatePassword = null,
        CancellationToken cancellationToken = default)
    {
        var credential = CredentialFactory.Create(profile, certificatePassword);
        var client = new GraphServiceClient(credential, [CredentialFactory.GraphScope]);

        try
        {
            var token = await credential.GetTokenAsync(
                new TokenRequestContext([CredentialFactory.GraphScope]),
                cancellationToken).ConfigureAwait(false);

            var roles = JwtRoleParser.ParseRoles(token.Token);

            return new DirectoryConnection
            {
                Reader = new GraphDirectoryReader(client),
                ClientId = profile.ClientId,
                GrantedPermissions = roles,
            };
        }
        catch (AuthenticationFailedException ex)
        {
            throw ToConnectException(ex);
        }
    }

    /// <summary>
    /// Wraps a sign-in failure so the AADSTS code and description (on the inner MSAL exception) are shown.
    /// </summary>
    private static DirectoryReadException ToConnectException(AuthenticationFailedException ex) =>
        new(
            $"Sign-in failed: {ex.Message}{(ex.InnerException is not null ? $" {ex.InnerException.Message}" : string.Empty)}",
            innerException: ex);
}
