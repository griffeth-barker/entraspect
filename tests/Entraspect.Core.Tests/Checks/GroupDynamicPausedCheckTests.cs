using Entraspect.Core.Checks;
using static Entraspect.Core.Tests.Checks.GroupTestData;

namespace Entraspect.Core.Tests.Checks;

public sealed class GroupDynamicPausedCheckTests
{
    private static Task<IReadOnlyList<Finding>> RunAsync(params DirectoryGroup[] groups) =>
        new GroupDynamicPausedCheck().RunAsync(new FakeDirectoryReader(groups), new CheckOptions());

    [Theory]
    [InlineData(GroupKind.Security)]
    [InlineData(GroupKind.Microsoft365)]
    public async Task Paused_dynamic_group_is_a_warning_about_the_group(GroupKind kind)
    {
        var f = Assert.Single(await RunAsync(Group("1", kind, isDynamic: true, isPaused: true)));

        Assert.Equal("group-dynamic-paused", f.CheckId);
        Assert.Equal(Severity.Warning, f.Severity);
        Assert.Equal("Group", f.ObjectType);
        Assert.Equal("1", f.ObjectId);
        Assert.Equal("Group 1", f.ObjectDisplayName);
        Assert.Equal(
            "Dynamic membership processing is paused, so members are no longer added or removed.",
            f.Message);
    }

    [Fact]
    public async Task Dynamic_group_that_is_processing_has_no_finding()
    {
        Assert.Empty(await RunAsync(Group("1", isDynamic: true, isPaused: false)));
    }

    [Fact]
    public async Task Paused_group_that_is_not_dynamic_has_no_finding()
    {
        Assert.Empty(await RunAsync(Group("1", isDynamic: false, isPaused: true)));
    }
}
