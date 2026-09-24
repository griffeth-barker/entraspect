namespace Entraspect.Core;

/// <summary>
/// The kind of directory object a check looks at. The enum order is the order of the navigation sections.
/// </summary>
public enum CheckCategory
{
    Users,
    Groups,
    AppRegistrations,
    EnterpriseApps,
    ConditionalAccess,
}
