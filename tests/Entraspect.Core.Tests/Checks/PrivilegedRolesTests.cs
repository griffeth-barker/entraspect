using System.Linq;
using Entraspect.Core.Checks;

namespace Entraspect.Core.Tests.Checks;

public sealed class PrivilegedRolesTests
{
    [Theory]
    [InlineData("62e90394-69f5-4237-9190-012177145e10")] // Global Administrator
    [InlineData("e8611ab8-c189-46e8-94e1-60213ab1f814")] // Privileged Role Administrator
    [InlineData("7be44c8a-adaf-4e2a-84d6-ab2649e08a13")] // Privileged Authentication Administrator
    [InlineData("8ac3fc64-6eca-42ea-9e69-59f4c7b60eb2")] // Hybrid Identity Administrator
    [InlineData("e00e864a-17c5-4a4b-9c06-f5b95a8d5bd8")] // Partner Tier2 Support
    public void Tenant_takeover_roles_are_tier_0(string templateId)
    {
        Assert.Equal(RoleTier.Tier0, PrivilegedRoles.TierOf(templateId));
    }

    [Theory]
    [InlineData("f2ef992c-3afb-46b9-b7cf-a126ee74c451")] // Global Reader
    [InlineData("fe930be7-5e62-47db-91af-98c3a49a38b1")] // User Administrator
    [InlineData("9b895d92-2cd3-44c7-9d02-a6ac2d5ea5c3")] // Application Administrator
    public void Other_roles_marked_privileged_are_privileged(string templateId)
    {
        Assert.Equal(RoleTier.Privileged, PrivilegedRoles.TierOf(templateId));
    }

    [Theory]
    [InlineData("88d8e3e3-8f55-4a1e-953a-9b9898b8876b")] // Directory Readers, not marked PRIVILEGED
    [InlineData("cccccccc-0000-0000-0000-000000000001")] // a custom role
    [InlineData("")]
    public void Any_other_role_is_other(string roleDefinitionId)
    {
        Assert.Equal(RoleTier.Other, PrivilegedRoles.TierOf(roleDefinitionId));
    }

    [Fact]
    public void Template_ids_match_regardless_of_case()
    {
        Assert.Equal(RoleTier.Tier0, PrivilegedRoles.TierOf(PrivilegedRoles.GlobalAdministrator.ToUpperInvariant()));
    }

    [Fact]
    public void Lists_every_role_microsoft_marks_privileged_once()
    {
        Assert.Equal(5, PrivilegedRoles.Tier0.Count);
        Assert.Equal(30, PrivilegedRoles.Privileged.Count);
        Assert.Empty(PrivilegedRoles.Tier0.Keys.Intersect(PrivilegedRoles.Privileged.Keys, StringComparer.OrdinalIgnoreCase));
        Assert.All(PrivilegedRoles.Tier0.Keys.Concat(PrivilegedRoles.Privileged.Keys), id => Assert.True(Guid.TryParse(id, out _)));
        Assert.Equal("Global Administrator", PrivilegedRoles.Tier0[PrivilegedRoles.GlobalAdministrator]);
    }
}
