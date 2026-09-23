using System.Collections.Generic;

namespace Entraspect.Core;

public sealed class AppRegistration
{
    public required string Id { get; init; }

    public required Guid AppId { get; init; }

    public required string DisplayName { get; init; }

    public required IReadOnlyList<AppCredential> Credentials { get; init; }
}
