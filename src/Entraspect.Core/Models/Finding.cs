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

    /// <summary>
    /// The user principal name, for findings about a user. Kept apart from <see cref="ObjectDisplayName"/>: a display
    /// name is for presentation and can repeat, while a UPN is unique in the tenant.
    /// </summary>
    public string? UserPrincipalName { get; init; }

    public required string Message { get; init; }

    public DateTimeOffset? DueDate { get; init; }

    /// <summary>Type of the item inside the object the finding is about, e.g. "Secret" for an app's client secret.</summary>
    public string? SubObjectType { get; init; }

    /// <summary>Display name of the item inside the object the finding is about, e.g. the credential's name.</summary>
    public string? SubObjectDisplayName { get; init; }

    /// <summary>
    /// The current value of the setting the finding is about, for example "Everyone", for checks that report tenant
    /// settings rather than objects.
    /// </summary>
    public string? Value { get; init; }
}
