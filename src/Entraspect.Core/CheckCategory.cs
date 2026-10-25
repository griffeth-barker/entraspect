namespace Entraspect.Core;

/// <summary>
/// The navigation section a check appears in: a kind of directory object, or a group of tenant settings such as
/// External Identities, as the Entra admin center groups them. The enum order is the order of the navigation sections.
/// </summary>
public enum CheckCategory
{
    Users,
    Groups,
    AppRegistrations,
    EnterpriseApps,
    RolesAndAdmins,
    ConditionalAccess,
    ExternalIdentities,
}
