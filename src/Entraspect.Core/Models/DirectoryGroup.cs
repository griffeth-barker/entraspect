using System.Collections.Generic;

namespace Entraspect.Core;

public enum GroupKind
{
    Microsoft365,
    Security,
    MailEnabledSecurity,
    Distribution,
}

/// <summary>Who can see and join a group, as Graph's <c>visibility</c> says.</summary>
public enum GroupVisibility
{
    Public,
    Private,
    HiddenMembership,
}

/// <summary>An error syncing the group from on-premises Active Directory, from <c>onPremisesProvisioningErrors</c>.</summary>
public sealed record GroupSyncError
{
    /// <summary>The kind of error, for example <c>PropertyConflict</c>. Empty when Graph gives none.</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>The property that caused the error, for example <c>ProxyAddresses</c>. Empty when Graph gives none.</summary>
    public string PropertyCausingError { get; init; } = string.Empty;

    /// <summary>The value that caused the error. Empty when Graph gives none.</summary>
    public string Value { get; init; } = string.Empty;

    public DateTimeOffset? OccurredDateTime { get; init; }
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
    /// How many owners the group has in Entra ID, at most 20: Graph returns at most 20 expanded owners, so 20 means
    /// 20 or more. For a synced security group, 0 means no owner in Entra ID; it may still have one in Active
    /// Directory (managedBy). For groups whose owners Graph documents as unavailable (distribution groups,
    /// mail-enabled security groups and other synced groups) it is the count when Graph returns an owner anyway, and
    /// null otherwise, since an empty list there doesn't mean no owner.
    /// </summary>
    public required int? OwnerCount { get; init; }

    /// <summary>Whether the group has at least one owner in Entra ID; null when <see cref="OwnerCount"/> is.</summary>
    public bool? HasOwners => OwnerCount is { } count ? count > 0 : null;

    /// <summary>Null when Graph returns no visibility or one entraspect doesn't know.</summary>
    public GroupVisibility? Visibility { get; init; }

    /// <summary>
    /// Whether dynamic membership processing is paused, so members are no longer added or removed. False for groups
    /// that aren't dynamic.
    /// </summary>
    public bool IsDynamicProcessingPaused { get; init; }

    /// <summary>Errors syncing the group from on-premises. Empty when there are none.</summary>
    public IReadOnlyList<GroupSyncError> SyncErrors { get; init; } = [];
}
