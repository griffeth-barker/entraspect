namespace Entraspect.Core;

/// <summary>What kind of directory object owns a group.</summary>
public enum GroupOwnerKind
{
    User,
    ServicePrincipal,

    /// <summary>Any other kind, or one Graph didn't name.</summary>
    Other,
}

/// <summary>An owner of a group, as Graph's list of group owners returns it.</summary>
public sealed class GroupOwner
{
    public required string Id { get; init; }

    public required GroupOwnerKind Kind { get; init; }

    /// <summary>The owner's name, or null when Graph left it out.</summary>
    public string? DisplayName { get; init; }

    /// <summary>The user principal name of a user owner; null for other kinds, or when Graph left it out.</summary>
    public string? UserPrincipalName { get; init; }
}
