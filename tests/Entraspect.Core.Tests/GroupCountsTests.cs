namespace Entraspect.Core.Tests;

public sealed class GroupCountsTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(20, true)]
    public void HasOwners_follows_the_owner_count(int? ownerCount, bool? expected)
    {
        var counts = new GroupCounts { GroupId = "g1", HasMembers = true, OwnerCount = ownerCount };

        Assert.Equal(expected, counts.HasOwners);
    }
}
