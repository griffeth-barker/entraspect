using Entraspect.Core.Checks;
using static Entraspect.Core.Tests.Checks.GroupTestData;

namespace Entraspect.Core.Tests.Checks;

public sealed class GroupPublicCheckTests
{
    private static Task<IReadOnlyList<Finding>> RunAsync(params DirectoryGroup[] groups) =>
        new GroupPublicCheck().RunAsync(new FakeDirectoryReader(groups), new CheckOptions());

    [Fact]
    public async Task Public_microsoft_365_group_is_info_about_the_group()
    {
        var f = Assert.Single(await RunAsync(Group("1", GroupKind.Microsoft365, visibility: GroupVisibility.Public)));

        Assert.Equal("group-public", f.CheckId);
        Assert.Equal(Severity.Info, f.Severity);
        Assert.Equal("Group", f.ObjectType);
        Assert.Equal("1", f.ObjectId);
        Assert.Equal("Group 1", f.ObjectDisplayName);
        Assert.Equal(
            "Anyone in the organisation can join this Microsoft 365 group and read its content.",
            f.Message);
    }

    [Theory]
    [InlineData(GroupVisibility.Private)]
    [InlineData(GroupVisibility.HiddenMembership)]
    [InlineData(null)]
    public async Task Microsoft_365_group_that_is_not_public_has_no_finding(GroupVisibility? visibility)
    {
        Assert.Empty(await RunAsync(Group("1", GroupKind.Microsoft365, visibility: visibility)));
    }

    [Theory]
    [InlineData(GroupKind.Security)]
    [InlineData(GroupKind.MailEnabledSecurity)]
    [InlineData(GroupKind.Distribution)]
    public async Task Public_group_of_other_kinds_has_no_finding(GroupKind kind)
    {
        Assert.Empty(await RunAsync(Group("1", kind, visibility: GroupVisibility.Public)));
    }

    [Fact]
    public async Task New_public_group_is_reported()
    {
        // The new group grace period is for groups still being set up; being public isn't something set up later.
        Assert.Single(await RunAsync(Group("1", GroupKind.Microsoft365, visibility: GroupVisibility.Public, created: Now)));
    }
}
