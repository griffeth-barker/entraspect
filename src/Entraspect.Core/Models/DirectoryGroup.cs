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
    /// Whether the group has at least one owner, or null when Graph doesn't return owners for this kind of group:
    /// groups synced from on-premises, distribution groups and mail-enabled security groups.
    /// </summary>
    public required bool? HasOwners { get; init; }
}
