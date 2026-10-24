namespace Entraspect.Core;

/// <summary>
/// Column headers for a check's findings, named after the properties of the object the check reads, for example
/// "Display name" and "Object ID" for an app registration. A null label hides that column.
/// </summary>
public sealed record FindingLabels
{
    /// <summary>Generic labels for checks that don't set their own.</summary>
    public static readonly FindingLabels Default = new();

    public string ObjectDisplayName { get; init; } = "Display name";

    public string? ObjectId { get; init; } = "Object ID";

    public string? SubObjectDisplayName { get; init; } = "Item";

    public string? SubObjectType { get; init; } = "Type";

    public string? DueDate { get; init; } = "Due";
}
