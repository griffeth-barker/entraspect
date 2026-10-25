namespace Entraspect.Core;

public enum GroupKind
{
    Microsoft365,
    Security,
    MailEnabledSecurity,
    Distribution,
}

public sealed class DirectoryGroup
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required GroupKind Kind { get; init; }

    public required bool IsDynamic { get; init; }

    public required bool IsSyncedFromOnPremises { get; init; }

    public required bool IsRoleAssignable { get; init; }

    public DateTimeOffset? CreatedDateTime { get; init; }

    /// <summary>Whether the group has at least one direct member.</summary>
    public required bool HasMembers { get; init; }

    /// <summary>
    /// Whether the group has at least one owner. For groups whose owners Graph documents as unavailable (synced from
    /// on-premises, distribution groups and mail-enabled security groups) it is true when Graph returns an owner
    /// anyway, and null otherwise, since an empty list there doesn't mean no owner.
    /// </summary>
    public required bool? HasOwners { get; init; }
}
