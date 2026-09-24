using System.Collections.Generic;
using System.Linq;

namespace Entraspect.Core.Tests;

public sealed class PermissionReviewTests
{
    [Fact]
    public void Missing_permissions_list_blocked_checks()
    {
        var check = new FakeCheck("c1", requiredPermissions: new[] { "A.Read.All" });
        var review = PermissionReview.Review(new[] { check }, new[] { "B.Read.All" });

        Assert.Single(review.MissingPermissions);
        Assert.Equal("A.Read.All", review.MissingPermissions[0].Permission);
        Assert.Equal(new[] { "c1" }, review.MissingPermissions[0].Checks.Select(c => c.Id));
    }

    [Fact]
    public void Granted_used_permission_is_not_unused()
    {
        var check = new FakeCheck("c1", requiredPermissions: new[] { "Application.Read.All" });
        var review = PermissionReview.Review(new[] { check }, new[] { "Application.Read.All" });

        Assert.Empty(review.MissingPermissions);
        Assert.Empty(review.UnusedReadPermissions);
        Assert.Empty(review.UnusedWritePermissions);
    }

    [Fact]
    public void Unused_read_permission_is_reported_as_read()
    {
        var check = new FakeCheck("c1", requiredPermissions: new[] { "Application.Read.All" });
        var review = PermissionReview.Review(new[] { check }, new[] { "Application.Read.All", "Directory.Read.All" });

        Assert.Equal(new[] { "Directory.Read.All" }, review.UnusedReadPermissions);
        Assert.Empty(review.UnusedWritePermissions);
    }

    [Fact]
    public void Unused_write_permission_is_reported_as_write()
    {
        var check = new FakeCheck("c1", requiredPermissions: new[] { "Application.Read.All" });
        var review = PermissionReview.Review(
            new[] { check },
            new[] { "Application.Read.All", "User.ReadWrite.All", "Mail.Send" });

        Assert.Equal(new[] { "User.ReadWrite.All", "Mail.Send" }, review.UnusedWritePermissions);
        Assert.Empty(review.UnusedReadPermissions);
    }
}
