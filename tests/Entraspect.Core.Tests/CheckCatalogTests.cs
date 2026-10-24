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
            new[] { CheckCategory.AppRegistrations },
            checks.Select(c => c.Category).Distinct());
        Assert.Contains(checks, c => c.Id == "app-credential-expiry");
        Assert.Contains(checks, c => c.RequiredPermissions.Contains("Application.Read.All"));
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
}
