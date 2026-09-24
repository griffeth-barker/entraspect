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
}
