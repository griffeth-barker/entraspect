namespace Entraspect.Core.Tests;

public sealed class DirectoryGroupTests
{
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
        };

        Assert.Null(group.Visibility);
        Assert.False(group.IsDynamicProcessingPaused);
        Assert.Empty(group.SyncErrors);
    }
}
