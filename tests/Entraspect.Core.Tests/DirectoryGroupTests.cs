namespace Entraspect.Core.Tests;

public sealed class DirectoryGroupTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(20, true)]
    public void HasOwners_follows_the_owner_count(int? ownerCount, bool? expected)
    {
        var group = new DirectoryGroup
        {
            Id = "g1",
            DisplayName = "Group",
            Kind = GroupKind.Security,
            IsDynamic = false,
            IsSyncedFromOnPremises = false,
            IsRoleAssignable = false,
            HasMembers = true,
            OwnerCount = ownerCount,
        };

        Assert.Equal(expected, group.HasOwners);
    }

    [Fact]
    public void New_properties_default_to_nothing_to_report()
    {
        var group = new DirectoryGroup
        {
            Id = "g1",
            DisplayName = "Group",
            Kind = GroupKind.Microsoft365,
            IsDynamic = true,
            IsSyncedFromOnPremises = false,
            IsRoleAssignable = false,
            HasMembers = true,
            OwnerCount = 1,
        };

        Assert.Null(group.Visibility);
        Assert.False(group.IsDynamicProcessingPaused);
        Assert.Empty(group.SyncErrors);
    }
}
