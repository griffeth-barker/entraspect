namespace Entraspect.Core;

public enum AppCredentialType
{
    Secret,
    Certificate,
}

public sealed class AppCredential
{
    public required Guid KeyId { get; init; }

    public required AppCredentialType Type { get; init; }

    public required string DisplayName { get; init; }

    public required DateTimeOffset StartDateTime { get; init; }

    public DateTimeOffset? EndDateTime { get; init; }
}
