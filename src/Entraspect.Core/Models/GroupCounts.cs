namespace Entraspect.Core;

/// <summary>
/// Whether a group has direct members, and how many owners it has, from <see cref="IDirectoryReader.GetGroupCountsAsync"/>.
/// Separate from <see cref="DirectoryGroup"/> because Graph needs <c>Group.Read.All</c> to count them, which the
/// other group checks don't.
/// </summary>
public sealed record GroupCounts
{
    public required string GroupId { get; init; }

    /// <summary>Whether the group has at least one direct member.</summary>
    public required bool HasMembers { get; init; }

    /// <summary>
    /// How many owners the group has in Entra ID, at most 20: Graph returns at most 20 expanded owners, so 20 means
    /// 20 or more. For a synced security group, 0 means no owner in Entra ID; it may still have one in Active
    /// Directory (managedBy). For groups whose owners Graph documents as unavailable (distribution groups,
    /// mail-enabled security groups and other synced groups) it is the count when Graph returns an owner anyway, and
    /// null otherwise, since an empty list there doesn't mean no owner.
    /// </summary>
    public required int? OwnerCount { get; init; }

    /// <summary>Whether the group has at least one owner in Entra ID; null when <see cref="OwnerCount"/> is.</summary>
    public bool? HasOwners => OwnerCount is { } count ? count > 0 : null;
}
