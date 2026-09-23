namespace Entraspect.Core;

public enum Severity
{
    Info,
    Warning,
    Critical,
}

public sealed class Finding
{
    public required string CheckId { get; init; }

    public required Severity Severity { get; init; }

    public required string ObjectType { get; init; }

    public required string ObjectId { get; init; }

    public required string ObjectDisplayName { get; init; }

    public required string Message { get; init; }

    public DateTimeOffset? DueDate { get; init; }

    public string? CredentialDisplayName { get; init; }

    public AppCredentialType? CredentialType { get; init; }
}
