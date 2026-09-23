using Entraspect.Core;
using Microsoft.Graph;

namespace Entraspect.Graph;

/// <summary>
/// Entry point for the App project: turns a connection profile into an <see cref="IDirectoryReader"/>
/// so no Graph SDK or Azure.Identity types leave this project.
/// </summary>
public static class DirectoryReaderFactory
{
    /// <param name="certificatePassword">Password for a .pfx file. Used once to load the certificate and never stored.</param>
    public static IDirectoryReader Create(ConnectionProfile profile, string? certificatePassword = null)
    {
        var credential = CredentialFactory.Create(profile, certificatePassword);
        var client = new GraphServiceClient(credential, [CredentialFactory.GraphScope]);
        return new GraphDirectoryReader(client);
    }
}
