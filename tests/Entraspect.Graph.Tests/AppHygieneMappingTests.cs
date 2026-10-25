using Entraspect.Core;
using Microsoft.Graph.Models;

namespace Entraspect.Graph.Tests;

// Mapping tests only: they build Graph SDK objects in memory and never create a client or credential.
public sealed class AppHygieneMappingTests
{
    private static readonly Guid MailSendId = Guid.Parse("b633e1c5-b582-4048-a93e-9f11b44c7e96");
    private static readonly Guid PrincipalId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly DateTimeOffset Created = new(2025, 3, 4, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MapApplication_maps_sign_in_audience_redirect_uris_and_owners()
    {
        var application = new Application
        {
            Id = "app-1",
            AppId = "11111111-1111-1111-1111-111111111111",
            DisplayName = "My App",
            SignInAudience = "AzureADMyOrg",
            IdentifierUris = ["urn:p2p_cert", ""],
            Web = new WebApplication { RedirectUris = ["https://contoso.com/signin-oidc", "http://example.com"] },
            Spa = new SpaApplication { RedirectUris = ["http://localhost:3000"] },
            PublicClient = new PublicClientApplication { RedirectUris = ["http://localhost"] },
        };

        var result = GraphDirectoryReader.MapApplication(application, hasOwners: false);

        Assert.Equal("AzureADMyOrg", result.SignInAudience);
        Assert.False(result.HasOwners);
        Assert.Equal(new[] { "urn:p2p_cert" }, result.IdentifierUris);
        Assert.Equal(
            new[]
            {
                new RedirectUri(RedirectUriPlatform.Web, "https://contoso.com/signin-oidc"),
                new RedirectUri(RedirectUriPlatform.Web, "http://example.com"),
                new RedirectUri(RedirectUriPlatform.Spa, "http://localhost:3000"),
                new RedirectUri(RedirectUriPlatform.PublicClient, "http://localhost"),
            },
            result.RedirectUris);
    }

    [Fact]
    public void MapApplication_without_platforms_has_no_redirect_uris()
    {
        var result = GraphDirectoryReader.MapApplication(
            new Application { Web = new WebApplication { RedirectUris = null } },
            hasOwners: true);

        Assert.Empty(result.RedirectUris);
        Assert.Empty(result.IdentifierUris);
        Assert.Null(result.SignInAudience);
        Assert.True(result.HasOwners);
    }

    [Fact]
    public void MapServicePrincipal_maps_the_owner_organization()
    {
        var owner = Guid.Parse("f8cdef31-a31e-4b4a-93e4-5f571e91255a");

        var app = GraphDirectoryReader.MapServicePrincipal(new ServicePrincipal { Id = "sp-1", AppOwnerOrganizationId = owner });

        Assert.Equal(owner, app.AppOwnerOrganizationId);
        Assert.Null(GraphDirectoryReader.MapServicePrincipal(new ServicePrincipal { Id = "sp-2" }).AppOwnerOrganizationId);
    }

    [Fact]
    public void MapAppRoleNames_maps_role_ids_to_values_and_skips_incomplete_roles()
    {
        var names = GraphDirectoryReader.MapAppRoleNames(
        [
            new AppRole { Id = MailSendId, Value = "Mail.Send" },
            new AppRole { Id = Guid.NewGuid(), Value = null },
            new AppRole { Id = null, Value = "User.Read.All" },
            new AppRole { Id = MailSendId, Value = "Duplicate" },
        ]);

        Assert.Equal(System.Collections.Generic.KeyValuePair.Create(MailSendId, "Mail.Send"), Assert.Single(names));
        Assert.Empty(GraphDirectoryReader.MapAppRoleNames(null));
    }

    [Fact]
    public void MapAppRoleAssignment_maps_a_service_principal_grant()
    {
        var grant = GraphDirectoryReader.MapAppRoleAssignment(
            new AppRoleAssignment
            {
                AppRoleId = MailSendId,
                PrincipalId = PrincipalId,
                PrincipalType = "ServicePrincipal",
                PrincipalDisplayName = "Mailer",
                CreatedDateTime = Created,
            },
            new Dictionary<Guid, string> { [MailSendId] = "Mail.Send" });

        Assert.NotNull(grant);
        Assert.Equal(PrincipalId.ToString(), grant.ServicePrincipalId);
        Assert.Equal("Mailer", grant.ServicePrincipalDisplayName);
        Assert.Equal("Mail.Send", grant.PermissionName);
        Assert.Equal(Created, grant.CreatedDateTime);
    }

    [Theory]
    [InlineData("User")]
    [InlineData("Group")]
    [InlineData(null)]
    public void MapAppRoleAssignment_skips_assignments_not_to_a_service_principal(string? principalType)
    {
        var grant = GraphDirectoryReader.MapAppRoleAssignment(
            new AppRoleAssignment { AppRoleId = MailSendId, PrincipalId = PrincipalId, PrincipalType = principalType },
            new Dictionary<Guid, string> { [MailSendId] = "Mail.Send" });

        Assert.Null(grant);
    }

    [Fact]
    public void MapAppRoleAssignment_with_unknown_role_or_missing_fields_does_not_throw()
    {
        var unknownRole = Guid.NewGuid();

        var grant = GraphDirectoryReader.MapAppRoleAssignment(
            new AppRoleAssignment { AppRoleId = unknownRole, PrincipalId = PrincipalId, PrincipalType = "servicePrincipal" },
            new Dictionary<Guid, string>());

        Assert.NotNull(grant);
        Assert.Equal(unknownRole.ToString(), grant.PermissionName);
        Assert.Equal(string.Empty, grant.ServicePrincipalDisplayName);
        Assert.Null(grant.CreatedDateTime);
        Assert.Null(GraphDirectoryReader.MapAppRoleAssignment(
            new AppRoleAssignment { PrincipalType = "ServicePrincipal" },
            new Dictionary<Guid, string>()));
    }
}
