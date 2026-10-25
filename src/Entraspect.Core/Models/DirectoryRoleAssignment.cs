namespace Entraspect.Core;

/// <summary>
/// An active directory role assignment. For built-in roles <see cref="RoleDefinitionId"/> is the role template ID that
/// Conditional Access policies use. The principal may be a user, a role-assignable group or a service principal.
/// </summary>
public sealed class DirectoryRoleAssignment
{
    public required string RoleDefinitionId { get; init; }

    public required string PrincipalId { get; init; }
}
