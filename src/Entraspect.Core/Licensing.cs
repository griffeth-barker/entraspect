namespace Entraspect.Core;

/// <summary>The licence a check needs from the tenant.</summary>
public enum LicenceRequirement
{
    None,
    EntraIdP1,
    EntraIdP2,
}

/// <summary>The highest Entra ID licence the tenant has, or <see cref="Unknown"/> when entraspect couldn't read it.</summary>
public enum TenantLicence
{
    Unknown,
    Free,
    EntraIdP1,
    EntraIdP2,
}

public static class Licensing
{
    /// <summary>
    /// Whether <paramref name="tenant"/> meets <paramref name="requirement"/>: the same licence or a higher one (P2
    /// meets P1). <see cref="TenantLicence.Unknown"/> meets every requirement, so the check runs and Graph says whether
    /// the licence is there.
    /// </summary>
    public static bool IsMet(LicenceRequirement requirement, TenantLicence tenant) =>
        requirement switch
        {
            LicenceRequirement.None => true,
            _ when tenant == TenantLicence.Unknown => true,
            LicenceRequirement.EntraIdP1 => tenant is TenantLicence.EntraIdP1 or TenantLicence.EntraIdP2,
            LicenceRequirement.EntraIdP2 => tenant == TenantLicence.EntraIdP2,
            _ => true,
        };

    /// <summary>Whether <paramref name="check"/> can run on a tenant with <paramref name="tenant"/>.</summary>
    public static bool IsLicensed(ICheck check, TenantLicence tenant) => IsMet(check.RequiredLicence, tenant);

    /// <summary>The short note for a check the tenant isn't licensed for, for example on Settings > Checks.</summary>
    public static string Needs(LicenceRequirement requirement) =>
        requirement switch
        {
            LicenceRequirement.EntraIdP2 => "Needs an Entra ID P2 licence.",
            _ => "Needs an Entra ID P1 or P2 licence.",
        };
}
