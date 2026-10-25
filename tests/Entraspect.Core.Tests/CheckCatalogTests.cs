using System.Linq;
using Microsoft.Extensions.Time.Testing;

namespace Entraspect.Core.Tests;

public sealed class CheckCatalogTests
{
    [Fact]
    public void All_lists_every_check_in_nav_order()
    {
        var checks = CheckCatalog.All(new FakeTimeProvider());

        Assert.Equal(
            new[]
            {
                "users-mfa-not-registered", "users-mfa-not-required", "group-no-members", "group-no-owners",
                "app-no-owners",
                "app-credential-expiry", "app-risky-redirect-uris", "saml-signing-certificate-expiry",
                "sp-high-privilege-app-permissions", "ca-policy-hygiene",
            },
            checks.Select(c => c.Id));
        Assert.Equal(
            new[]
            {
                CheckCategory.Users, CheckCategory.Users, CheckCategory.Groups, CheckCategory.Groups, CheckCategory.AppRegistrations,
                CheckCategory.AppRegistrations, CheckCategory.AppRegistrations, CheckCategory.EnterpriseApps,
                CheckCategory.EnterpriseApps, CheckCategory.ConditionalAccess,
            },
            checks.Select(c => c.Category));
        Assert.Contains(checks, c => c.RequiredPermissions.Contains("Application.Read.All"));
    }

    [Fact]
    public void Checks_are_sorted_by_title_within_each_category()
    {
        // The catalog order is the navigation order, and users scan a section by name.
        foreach (var category in CheckCatalog.All(new FakeTimeProvider()).GroupBy(c => c.Category))
        {
            var titles = category.Select(c => c.Title).ToList();
            Assert.Equal(titles.Order(StringComparer.CurrentCultureIgnoreCase), titles);
        }
    }

    [Fact]
    public void Ids_are_unique()
    {
        var checks = CheckCatalog.All(new FakeTimeProvider());

        Assert.Equal(checks.Count, checks.Select(c => c.Id).Distinct().Count());
    }

    [Fact]
    public void Only_read_permissions_are_required()
    {
        var permissions = CheckCatalog.All(new FakeTimeProvider()).SelectMany(c => c.RequiredPermissions).Distinct();

        Assert.Equal(
            new[]
            {
                "AuditLog.Read.All", "Policy.Read.All", "User.Read.All", "GroupMember.Read.All",
                "RoleManagement.Read.Directory", "Member.Read.Hidden", "Application.Read.All",
            },
            permissions);
    }

    [Fact]
    public void Titles_are_short_enough_for_the_navigation_panel()
    {
        // The title is the nav label, shown indented under its section next to a badge.
        foreach (var check in CheckCatalog.All(new FakeTimeProvider()))
        {
            Assert.True(
                check.Title.Length <= CheckCatalog.MaxTitleLength,
                $"'{check.Title}' is longer than {CheckCatalog.MaxTitleLength} characters.");
        }
    }

    [Fact]
    public void Expiring_credentials_columns_are_named_like_the_app_registration()
    {
        var check = CheckCatalog.All(new FakeTimeProvider()).Single(c => c.Id == "app-credential-expiry");

        Assert.Equal(
            ("Display name", "Object ID", "Credential", "Type", "Expires"),
            (check.Labels.ObjectDisplayName, check.Labels.ObjectId, check.Labels.SubObjectDisplayName,
                check.Labels.SubObjectType, check.Labels.DueDate));
    }

    [Fact]
    public void Expiring_saml_certificates_columns_are_named_like_the_enterprise_app()
    {
        var check = CheckCatalog.All(new FakeTimeProvider()).Single(c => c.Id == "saml-signing-certificate-expiry");

        Assert.Equal("Expiring SAML certificates", check.Title);
        Assert.Equal(
            ("Display name", "Object ID", "Certificate", (string?)null, "Expires"),
            (check.Labels.ObjectDisplayName, check.Labels.ObjectId, check.Labels.SubObjectDisplayName,
                check.Labels.SubObjectType, check.Labels.DueDate));
        Assert.Equal(new[] { "Application.Read.All" }, check.RequiredPermissions);
    }

    [Fact]
    public void Apps_with_no_owners_columns_are_named_like_the_app_registration_list()
    {
        var check = CheckCatalog.All(new FakeTimeProvider()).Single(c => c.Id == "app-no-owners");

        Assert.Equal("Apps with no owners", check.Title);
        Assert.Equal(CheckCategory.AppRegistrations, check.Category);
        Assert.Equal(("Display name", "Object ID"), (check.Labels.ObjectDisplayName, check.Labels.ObjectId));
        Assert.Null(check.Labels.UserPrincipalName);
        Assert.Null(check.Labels.SubObjectDisplayName);
        Assert.Null(check.Labels.SubObjectType);
        Assert.Null(check.Labels.DueDate);
        Assert.Equal(new[] { "Application.Read.All" }, check.RequiredPermissions);
    }

    [Fact]
    public void Risky_redirect_uris_columns_are_named_like_the_authentication_page()
    {
        var check = CheckCatalog.All(new FakeTimeProvider()).Single(c => c.Id == "app-risky-redirect-uris");

        Assert.Equal("Risky redirect URIs", check.Title);
        Assert.Equal(CheckCategory.AppRegistrations, check.Category);
        Assert.Equal(
            ("Display name", "Object ID", "Redirect URI", "Platform", (string?)null),
            (check.Labels.ObjectDisplayName, check.Labels.ObjectId, check.Labels.SubObjectDisplayName,
                check.Labels.SubObjectType, check.Labels.DueDate));
        Assert.Equal(new[] { "Application.Read.All" }, check.RequiredPermissions);
    }

    [Fact]
    public void High_privilege_apps_columns_are_named_like_the_enterprise_app_permissions_page()
    {
        var check = CheckCatalog.All(new FakeTimeProvider()).Single(c => c.Id == "sp-high-privilege-app-permissions");

        Assert.Equal("High-privilege apps", check.Title);
        Assert.Equal(CheckCategory.EnterpriseApps, check.Category);
        Assert.Equal(
            ("Display name", "Object ID", "Permission", (string?)null, "Granted"),
            (check.Labels.ObjectDisplayName, check.Labels.ObjectId, check.Labels.SubObjectDisplayName,
                check.Labels.SubObjectType, check.Labels.DueDate));
        Assert.Equal(new[] { "Application.Read.All" }, check.RequiredPermissions);
    }

    [Fact]
    public void Mfa_not_registered_columns_are_named_like_the_user_list()
    {
        var check = CheckCatalog.All(new FakeTimeProvider()).Single(c => c.Id == "users-mfa-not-registered");

        Assert.Equal("MFA not registered", check.Title);
        Assert.Equal(CheckCategory.Users, check.Category);
        Assert.Equal(
            ("Display name", "User principal name", "Object ID"),
            (check.Labels.ObjectDisplayName, check.Labels.UserPrincipalName, check.Labels.ObjectId));
        Assert.Null(check.Labels.SubObjectDisplayName);
        Assert.Null(check.Labels.SubObjectType);
        Assert.Null(check.Labels.DueDate);
        Assert.Equal(new[] { "AuditLog.Read.All" }, check.RequiredPermissions);
    }

    [Fact]
    public void Mfa_not_required_columns_are_named_like_the_user_list()
    {
        var check = CheckCatalog.All(new FakeTimeProvider()).Single(c => c.Id == "users-mfa-not-required");

        Assert.Equal("MFA not required", check.Title);
        Assert.Equal(CheckCategory.Users, check.Category);
        Assert.Equal(LicenceRequirement.EntraIdP1, check.RequiredLicence);
        Assert.Contains("who isn't required to use MFA", check.Description);
        Assert.Contains("beta", check.Description);
        Assert.Equal(
            ("Display name", "User principal name", "Object ID", "Last single-factor sign-in"),
            (check.Labels.ObjectDisplayName, check.Labels.UserPrincipalName, check.Labels.ObjectId,
                check.Labels.DueDate));
        Assert.Null(check.Labels.SubObjectDisplayName);
        Assert.Null(check.Labels.SubObjectType);
        Assert.Equal(
            new[]
            {
                "Policy.Read.All", "User.Read.All", "GroupMember.Read.All", "RoleManagement.Read.Directory",
                "AuditLog.Read.All",
            },
            check.RequiredPermissions);
    }

    [Theory]
    [InlineData("group-no-members", new[] { "GroupMember.Read.All", "Member.Read.Hidden" })]
    [InlineData("group-no-owners", new[] { "GroupMember.Read.All" })]
    public void Group_check_columns_are_named_like_the_group_list(string id, string[] permissions)
    {
        var check = CheckCatalog.All(new FakeTimeProvider()).Single(c => c.Id == id);

        Assert.Equal(("Display name", "Object ID"), (check.Labels.ObjectDisplayName, check.Labels.ObjectId));
        Assert.Null(check.Labels.UserPrincipalName);
        Assert.Null(check.Labels.SubObjectDisplayName);
        Assert.Null(check.Labels.SubObjectType);
        Assert.Null(check.Labels.DueDate);
        Assert.Equal(permissions, check.RequiredPermissions);
    }

    [Fact]
    public void Policy_hygiene_columns_are_named_like_the_conditional_access_policy_list()
    {
        var check = CheckCatalog.All(new FakeTimeProvider()).Single(c => c.Id == "ca-policy-hygiene");

        Assert.Equal("Policy hygiene", check.Title);
        Assert.Equal(CheckCategory.ConditionalAccess, check.Category);
        Assert.Equal(LicenceRequirement.EntraIdP1, check.RequiredLicence);
        Assert.Equal(
            ("Policy name", "Policy ID", "Deleted object ID", "Issue", "Modified date"),
            (check.Labels.ObjectDisplayName, check.Labels.ObjectId, check.Labels.SubObjectDisplayName,
                check.Labels.SubObjectType, check.Labels.DueDate));
        Assert.Null(check.Labels.UserPrincipalName);
        Assert.Equal(new[] { "Policy.Read.All", "User.Read.All", "GroupMember.Read.All" }, check.RequiredPermissions);
    }
}
