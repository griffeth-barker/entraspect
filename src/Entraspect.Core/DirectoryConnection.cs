using System.Collections.Generic;

namespace Entraspect.Core;

/// <summary>
/// A connected tenant: the reader to use, the connected client ID, and the application permissions the token grants.
/// Returned by <c>DirectoryConnector.ConnectAsync</c> so the App never sees Graph SDK or Azure.Identity types.
/// </summary>
public sealed class DirectoryConnection
{
    public required IDirectoryReader Reader { get; init; }

    public required string ClientId { get; init; }

    public required IReadOnlyList<string> GrantedPermissions { get; init; }
}
