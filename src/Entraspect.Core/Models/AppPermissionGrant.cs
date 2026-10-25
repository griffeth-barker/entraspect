namespace Entraspect.Core;

/// <summary>A Microsoft Graph application permission (app role assignment) granted to a service principal.</summary>
public sealed class AppPermissionGrant
{
    public required string ServicePrincipalId { get; init; }

    public required string ServicePrincipalDisplayName { get; init; }

    /// <summary>The app role's value, for example <c>Mail.Send</c>.</summary>
    public required string PermissionName { get; init; }

    public DateTimeOffset? CreatedDateTime { get; init; }
}
