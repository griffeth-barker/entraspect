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
            new[] { "group-no-members", "group-no-owners", "app-credential-expiry" },
            checks.Select(c => c.Id));
        Assert.Equal(
            new[] { CheckCategory.Groups, CheckCategory.Groups, CheckCategory.AppRegistrations },
            checks.Select(c => c.Category));
        Assert.Contains(checks, c => c.RequiredPermissions.Contains("Application.Read.All"));
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

        Assert.Equal(new[] { "GroupMember.Read.All", "Member.Read.Hidden", "Application.Read.All" }, permissions);
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

    [Theory]
    [InlineData("group-no-members", new[] { "GroupMember.Read.All", "Member.Read.Hidden" })]
    [InlineData("group-no-owners", new[] { "GroupMember.Read.All" })]
    public void Group_check_columns_are_named_like_the_group_list(string id, string[] permissions)
    {
        var check = CheckCatalog.All(new FakeTimeProvider()).Single(c => c.Id == id);

        Assert.Equal(("Display name", "Object ID"), (check.Labels.ObjectDisplayName, check.Labels.ObjectId));
        Assert.Null(check.Labels.SubObjectDisplayName);
        Assert.Null(check.Labels.SubObjectType);
        Assert.Null(check.Labels.DueDate);
        Assert.Equal(permissions, check.RequiredPermissions);
    }
}
