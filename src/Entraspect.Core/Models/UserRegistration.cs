using System.Collections.Generic;

namespace Entraspect.Core;

public enum UserType
{
    Member,
    Guest,
}

/// <summary>
/// A user's authentication method registration, from the Authentication methods user registration details report.
/// Includes users synced from on-premises Active Directory.
/// </summary>
public sealed class UserRegistration
{
    public required string Id { get; init; }

    public required string UserPrincipalName { get; init; }

    public required string DisplayName { get; init; }

    public required UserType UserType { get; init; }

    /// <summary>Whether the user holds an admin role, as the report defines it.</summary>
    public required bool IsAdmin { get; init; }

    /// <summary>Whether the user has registered any multifactor authentication method.</summary>
    public required bool IsMfaRegistered { get; init; }

    /// <summary>Whether the user has registered a strong authentication method that the current policy allows.</summary>
    public required bool IsMfaCapable { get; init; }

    /// <summary>The registered methods as the report names them, for example <c>microsoftAuthenticatorPush</c>.</summary>
    public required IReadOnlyList<string> MethodsRegistered { get; init; }

    /// <summary>When the report last updated this user's record.</summary>
    public DateTimeOffset? LastUpdatedDateTime { get; init; }
}
