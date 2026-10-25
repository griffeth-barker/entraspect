using Entraspect.Core.Checks;

namespace Entraspect.Core.Tests.Checks;

public sealed class HighPrivilegeAppPermissionsCheckTests
{
    private static readonly DateTimeOffset Granted = new(2025, 3, 4, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid CustomerTenantId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private static EnterpriseApp App(string id, Guid? ownerTenantId) =>
        new()
        {
            Id = id,
            AppId = Guid.NewGuid(),
            DisplayName = $"App {id}",
            AccountEnabled = true,
            AppOwnerOrganizationId = ownerTenantId,
            SigningCertificates = [],
        };

    private static AppPermissionGrant Grant(string servicePrincipalId, string permission) =>
        new()
        {
            ServicePrincipalId = servicePrincipalId,
            ServicePrincipalDisplayName = $"App {servicePrincipalId}",
            PermissionName = permission,
            CreatedDateTime = Granted,
        };

    private static Task<IReadOnlyList<Finding>> RunAsync(
        IReadOnlyList<EnterpriseApp> apps,
        params AppPermissionGrant[] grants) =>
        new HighPrivilegeAppPermissionsCheck().RunAsync(
            new FakeDirectoryReader([], enterpriseApps: apps, grants: grants),
            new CheckOptions());

    public static TheoryData<string> CriticalPermissions => new(HighPrivilegePermissions.Critical);

    public static TheoryData<string> WarningPermissions => new(HighPrivilegePermissions.Warning);

    [Theory]
    [MemberData(nameof(CriticalPermissions))]
    public async Task Tenant_takeover_permission_is_critical(string permission)
    {
        var f = Assert.Single(await RunAsync([App("1", CustomerTenantId)], Grant("1", permission)));

        Assert.Equal(Severity.Critical, f.Severity);
    }

    [Theory]
    [MemberData(nameof(WarningPermissions))]
    public async Task Tenant_wide_data_permission_is_a_warning(string permission)
    {
        var f = Assert.Single(await RunAsync([App("1", CustomerTenantId)], Grant("1", permission)));

        Assert.Equal(Severity.Warning, f.Severity);
    }

    [Fact]
    public void Tiers_list_the_spec_permissions()
    {
        Assert.Equal(9, HighPrivilegePermissions.Critical.Count);
        Assert.Equal(8, HighPrivilegePermissions.Warning.Count);
        Assert.Empty(HighPrivilegePermissions.Critical.Intersect(HighPrivilegePermissions.Warning));
        Assert.Contains("RoleManagement.ReadWrite.Directory", HighPrivilegePermissions.Critical);
        Assert.Contains("Mail.Send", HighPrivilegePermissions.Warning);
    }

    [Theory]
    [InlineData("User.Read.All")]
    [InlineData("Application.Read.All")]
    [InlineData("Mail.Read")]
    [InlineData("Directory.Read.All")]
    [InlineData("11111111-1111-1111-1111-111111111111")]
    public async Task Permission_in_neither_list_has_no_finding(string permission)
    {
        Assert.Empty(await RunAsync([App("1", CustomerTenantId)], Grant("1", permission)));
    }

    [Fact]
    public async Task Microsoft_owned_service_principal_is_skipped()
    {
        Assert.Empty(await RunAsync(
            [App("1", HighPrivilegeAppPermissionsCheck.MicrosoftTenantId)],
            Grant("1", "Directory.ReadWrite.All")));
    }

    [Fact]
    public async Task Service_principal_without_owner_organization_or_not_listed_is_checked()
    {
        var findings = await RunAsync(
            [App("1", null)],
            Grant("1", "Mail.Send"),
            Grant("missing", "Mail.Send"));

        Assert.Equal(new[] { "1", "missing" }, findings.Select(f => f.ObjectId));
    }

    [Fact]
    public async Task Finding_is_about_the_enterprise_app_and_names_the_permission()
    {
        var f = Assert.Single(await RunAsync([App("1", CustomerTenantId)], Grant("1", "Mail.Send")));

        Assert.Equal("sp-high-privilege-app-permissions", f.CheckId);
        Assert.Equal("EnterpriseApp", f.ObjectType);
        Assert.Equal("1", f.ObjectId);
        Assert.Equal("App 1", f.ObjectDisplayName);
        Assert.Equal("Application permission", f.SubObjectType);
        Assert.Equal("Mail.Send", f.SubObjectDisplayName);
        Assert.Equal("Has Microsoft Graph application permission Mail.Send.", f.Message);
        Assert.Equal(Granted, f.DueDate);
    }

    [Fact]
    public async Task Each_matching_grant_gives_one_finding()
    {
        var findings = await RunAsync(
            [App("1", CustomerTenantId), App("2", CustomerTenantId)],
            Grant("1", "Mail.Send"),
            Grant("1", "Application.ReadWrite.All"),
            Grant("1", "User.Read.All"),
            Grant("2", "Sites.FullControl.All"));

        Assert.Equal(
            new (string, string?)[] { ("1", "Mail.Send"), ("1", "Application.ReadWrite.All"), ("2", "Sites.FullControl.All") },
            findings.Select(f => (f.ObjectId, f.SubObjectDisplayName)));
    }
}
