using Entraspect.Core;
using Entraspect.Graph;
using Microsoft.Graph;

namespace Entraspect.App.Services;

public static class ReaderFactory
{
    public static IDirectoryReader Create(ConnectionProfile profile, string? certificatePassword = null)
    {
        var credential = CredentialFactory.Create(profile, certificatePassword);
        var client = CredentialFactory.CreateGraphClient(credential);
        return new GraphDirectoryReader(client);
    }
}
