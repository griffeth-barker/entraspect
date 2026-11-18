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
