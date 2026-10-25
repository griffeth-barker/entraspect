using Microsoft.Extensions.Time.Testing;

namespace Entraspect.Core.Tests;

public sealed class LicensingTests
{
    [Theory]
    [InlineData(LicenceRequirement.None, TenantLicence.Unknown, true)]
    [InlineData(LicenceRequirement.None, TenantLicence.Free, true)]
    [InlineData(LicenceRequirement.None, TenantLicence.EntraIdP1, true)]
    [InlineData(LicenceRequirement.None, TenantLicence.EntraIdP2, true)]
    [InlineData(LicenceRequirement.EntraIdP1, TenantLicence.Unknown, true)]
    [InlineData(LicenceRequirement.EntraIdP1, TenantLicence.Free, false)]
    [InlineData(LicenceRequirement.EntraIdP1, TenantLicence.EntraIdP1, true)]
    [InlineData(LicenceRequirement.EntraIdP1, TenantLicence.EntraIdP2, true)]
    [InlineData(LicenceRequirement.EntraIdP2, TenantLicence.Unknown, true)]
    [InlineData(LicenceRequirement.EntraIdP2, TenantLicence.Free, false)]
    [InlineData(LicenceRequirement.EntraIdP2, TenantLicence.EntraIdP1, false)]
    [InlineData(LicenceRequirement.EntraIdP2, TenantLicence.EntraIdP2, true)]
    public void Requirement_is_met_by_the_same_or_a_higher_licence(
        LicenceRequirement requirement,
        TenantLicence tenant,
        bool expected)
    {
        Assert.Equal(expected, Licensing.IsMet(requirement, tenant));
    }

    [Fact]
    public void Checks_need_no_licence_unless_they_say_so()
    {
        var checks = CheckCatalog.All(new FakeTimeProvider());

        string[] licensed =
        [
            "guests-inactive", "users-inactive", "users-mfa-not-registered", "users-mfa-not-required",
            "group-license-errors", "roles-admins-no-phishing-resistant-mfa", "ca-policy-hygiene",
            "ca-unused-policies",
        ];
        string[] p2 = ["users-risky"];
        Assert.All(
            checks.Where(c => licensed.Contains(c.Id)),
            c => Assert.Equal(LicenceRequirement.EntraIdP1, c.RequiredLicence));
        Assert.All(
            checks.Where(c => p2.Contains(c.Id)),
            c => Assert.Equal(LicenceRequirement.EntraIdP2, c.RequiredLicence));
        Assert.All(
            checks.Where(c => !licensed.Contains(c.Id) && !p2.Contains(c.Id)),
            c => Assert.Equal(LicenceRequirement.None, c.RequiredLicence));
    }

    [Fact]
    public void Unlicensed_check_is_not_licensed_on_a_free_tenant()
    {
        var check = CheckCatalog.All(new FakeTimeProvider()).Single(c => c.Id == "users-mfa-not-registered");

        Assert.False(Licensing.IsLicensed(check, TenantLicence.Free));
        Assert.True(Licensing.IsLicensed(check, TenantLicence.Unknown));
    }

    [Theory]
    [InlineData(LicenceRequirement.EntraIdP1, "Needs an Entra ID P1 or P2 licence.")]
    [InlineData(LicenceRequirement.EntraIdP2, "Needs an Entra ID P2 licence.")]
    public void Note_names_the_licence(LicenceRequirement requirement, string expected)
    {
        Assert.Equal(expected, Licensing.Needs(requirement));
    }
}
