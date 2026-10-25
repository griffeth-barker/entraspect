namespace Entraspect.Core;

/// <summary>A user in the tenant, including users synced from on-premises Active Directory.</summary>
public sealed class DirectoryUser
{
    public required string Id { get; init; }

    public required string UserPrincipalName { get; init; }

    public required string DisplayName { get; init; }

    public required UserType UserType { get; init; }

    public required bool AccountEnabled { get; init; }
}
