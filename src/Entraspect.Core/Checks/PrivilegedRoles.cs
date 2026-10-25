using System.Collections.Generic;

namespace Entraspect.Core.Checks;

/// <summary>How much a directory role can do. The values ascend, so the highest tier compares greatest.</summary>
public enum RoleTier
{
    /// <summary>Any other role, including every custom role.</summary>
    Other,

    /// <summary>A built-in role Microsoft marks PRIVILEGED.</summary>
    Privileged,

    /// <summary>A role that can take over the tenant.</summary>
    Tier0,
}

/// <summary>
/// Built-in directory roles by template ID, in two tiers. Graph v1.0 has no "privileged" flag on role definitions
/// (<c>isPrivileged</c> is beta only), so the list is kept here. Custom roles are never privileged here, even when they
/// hold privileged permissions.
/// </summary>
/// <remarks>
/// Template IDs and the PRIVILEGED label from "Microsoft Entra built-in roles",
/// https://learn.microsoft.com/en-us/entra/identity/role-based-access-control/permissions-reference, read 2026-10-25.
/// </remarks>
public static class PrivilegedRoles
{
    /// <summary>The template ID of Global Administrator.</summary>
    public const string GlobalAdministrator = "62e90394-69f5-4237-9190-012177145e10";

    /// <summary>Roles that can take over the tenant, by template ID, with their names.</summary>
    public static IReadOnlyDictionary<string, string> Tier0 { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [GlobalAdministrator] = "Global Administrator",
            ["e8611ab8-c189-46e8-94e1-60213ab1f814"] = "Privileged Role Administrator",
            ["7be44c8a-adaf-4e2a-84d6-ab2649e08a13"] = "Privileged Authentication Administrator",
            ["8ac3fc64-6eca-42ea-9e69-59f4c7b60eb2"] = "Hybrid Identity Administrator",
            ["e00e864a-17c5-4a4b-9c06-f5b95a8d5bd8"] = "Partner Tier2 Support",
        };

    /// <summary>Every other built-in role Microsoft marks PRIVILEGED, by template ID, with their names.</summary>
    public static IReadOnlyDictionary<string, string> Privileged { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["db506228-d27e-4b7d-95e5-295956d6615f"] = "Agent ID Administrator",
            ["d2562ede-74db-457e-a7b6-544e236ebb61"] = "AI Administrator",
            ["1fe13547-53f6-408d-ac04-7f8eed167b38"] = "AI Reader",
            ["9b895d92-2cd3-44c7-9d02-a6ac2d5ea5c3"] = "Application Administrator",
            ["cf1c38e5-3621-4004-a7cb-879624dced7c"] = "Application Developer",
            ["ecb2c6bf-0ab6-418e-bd87-7986f8d63bbe"] = "Attribute Provisioning Administrator",
            ["422218e4-db15-4ef9-bbe0-8afb41546d79"] = "Attribute Provisioning Reader",
            ["c4e39bd9-1100-46d3-8c65-fb160da0071f"] = "Authentication Administrator",
            ["25a516ed-2fa0-40ea-a2d0-12923a21473a"] = "Authentication Extensibility Administrator",
            ["0b00bede-4072-4d22-b441-e7df02a1ef63"] = "Authentication Extensibility Password Administrator",
            ["aaf43236-0c0d-4d5f-883a-6955382ac081"] = "B2C IEF Keyset Administrator",
            ["158c047a-c907-4556-b7ef-446551a6b5f7"] = "Cloud Application Administrator",
            ["7698a772-787b-4ac8-901f-60d6b08affd2"] = "Cloud Device Administrator",
            ["b1be1c3e-b65d-4f19-8427-f6fa0d97feb9"] = "Conditional Access Administrator",
            ["9360feb5-f418-4baa-8175-e2a00bac4301"] = "Directory Writers",
            ["8329153b-31d0-4727-b945-745eb3bc5f31"] = "Domain Name Administrator",
            ["58f930cc-fcf4-4152-852c-1d7dbf502139"] = "Entra SOC Identity Responder",
            ["be2f45a1-457d-42af-a067-6ec1fa63bc45"] = "External Identity Provider Administrator",
            ["f2ef992c-3afb-46b9-b7cf-a126ee74c451"] = "Global Reader",
            ["729827e3-9c14-49f7-bb1b-9608f156bbb8"] = "Helpdesk Administrator",
            ["45d8d3c5-c802-45c6-b32a-1d70b5e1e86e"] = "Identity Governance Administrator",
            ["3a2c62db-5318-420d-8d74-23affee5d9d5"] = "Intune Administrator",
            ["59d46f88-662b-457b-bceb-5c3809e5908f"] = "Lifecycle Workflows Administrator",
            ["4ba39ca4-527c-499a-b93d-d9b492c50246"] = "Partner Tier1 Support",
            ["966707d0-3269-4727-9be2-8c3a10f19b9d"] = "Password Administrator",
            ["194ae4cb-b126-40b2-bd5b-6091b380977d"] = "Security Administrator",
            ["5f2222b1-57c3-48ba-8ad5-d4759f1fde6f"] = "Security Operator",
            ["5d6b6bb7-de71-4623-b4af-96380a352509"] = "Security Reader",
            ["1981f584-96e9-4a6f-95b0-f522373f8fae"] = "Tenant Governance Administrator",
            ["fe930be7-5e62-47db-91af-98c3a49a38b1"] = "User Administrator",
        };

    /// <summary>
    /// The tier of the role with this ID: for built-in roles, the template ID role assignments use. Any other ID,
    /// including a custom role's, is <see cref="RoleTier.Other"/>.
    /// </summary>
    public static RoleTier TierOf(string roleDefinitionId) =>
        Tier0.ContainsKey(roleDefinitionId) ? RoleTier.Tier0
        : Privileged.ContainsKey(roleDefinitionId) ? RoleTier.Privileged
        : RoleTier.Other;
}
