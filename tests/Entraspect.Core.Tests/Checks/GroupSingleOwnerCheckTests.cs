using Entraspect.Core.Checks;
using Microsoft.Extensions.Time.Testing;
using static Entraspect.Core.Tests.Checks.GroupTestData;

namespace Entraspect.Core.Tests.Checks;

public sealed class GroupSingleOwnerCheckTests
{
    private readonly FakeTimeProvider _time = new(Now);

    private Task<IReadOnlyList<Finding>> RunAsync(params DirectoryGroup[] groups) =>
        RunAsync(new CheckOptions(), groups);

    private async Task<IReadOnlyList<Finding>> RunAsync(CheckOptions options, params DirectoryGroup[] groups)
    {
        var check = new GroupSingleOwnerCheck(_time);
        return await check.RunAsync(new FakeDirectoryReader(groups), options);
    }

    [Fact]
    public async Task Microsoft_365_group_with_one_owner_is_info_about_the_group()
    {
        var f = Assert.Single(await RunAsync(Group("1", GroupKind.Microsoft365, ownerCount: 1)));

        Assert.Equal("group-single-owner", f.CheckId);
        Assert.Equal(Severity.Info, f.Severity);
        Assert.Equal("Group", f.ObjectType);
        Assert.Equal("1", f.ObjectId);
        Assert.Equal("Group 1", f.ObjectDisplayName);
        Assert.Equal(
            "Microsoft 365 group has only one owner. If they leave, no one can manage its membership.",
            f.Message);
        Assert.Null(f.SubObjectDisplayName);
        Assert.Null(f.SubObjectType);
        Assert.Null(f.DueDate);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Security_group_with_one_owner_is_info_whether_or_not_synced(bool synced)
    {
        var f = Assert.Single(await RunAsync(Group("1", GroupKind.Security, isSynced: synced, ownerCount: 1)));

        Assert.Equal(Severity.Info, f.Severity);
        Assert.Equal("Security group has only one owner.", f.Message);
    }

    [Theory]
    [InlineData(GroupKind.MailEnabledSecurity)]
    [InlineData(GroupKind.Distribution)]
    public async Task Other_kinds_with_one_owner_have_no_finding(GroupKind kind)
    {
        Assert.Empty(await RunAsync(Group("1", kind, ownerCount: 1)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(20)]
    public async Task No_owners_or_several_owners_have_no_finding(int ownerCount)
    {
        // 0 is reported by group-no-owners; 20 is the expand limit and means 20 or more.
        Assert.Empty(await RunAsync(
            Group("1", GroupKind.Microsoft365, ownerCount: ownerCount),
            Group("2", GroupKind.Security, ownerCount: ownerCount)));
    }

    [Fact]
    public async Task Unknown_owners_have_no_finding()
    {
        Assert.Empty(await RunAsync(
            Group("1", GroupKind.Microsoft365, hasOwners: null),
            Group("2", GroupKind.Security, isSynced: true, hasOwners: null)));
    }

    [Fact]
    public async Task Dynamic_group_with_one_owner_is_reported()
    {
        Assert.Single(await RunAsync(Group("1", GroupKind.Security, isDynamic: true, ownerCount: 1)));
    }

    [Fact]
    public async Task Group_created_exactly_grace_days_ago_is_checked()
    {
        var created = new DateTimeOffset(2026, 1, 8, 23, 0, 0, TimeSpan.Zero);

        Assert.Single(await RunAsync(Group("1", ownerCount: 1, created: created)));
    }

    [Fact]
    public async Task Group_inside_the_grace_period_is_skipped()
    {
        var created = new DateTimeOffset(2026, 1, 9, 0, 0, 0, TimeSpan.Zero);

        Assert.Empty(await RunAsync(
            Group("1", ownerCount: 1, created: created),
            Group("2", GroupKind.Security, isSynced: true, ownerCount: 1, created: created)));
    }

    [Fact]
    public async Task Custom_grace_days_are_used()
    {
        var options = new CheckOptions { NewGroupGraceDays = 30 };

        Assert.Empty(await RunAsync(options, Group("1", ownerCount: 1, created: Now.AddDays(-20))));
    }

    [Fact]
    public async Task Group_without_created_date_is_checked()
    {
        Assert.Single(await RunAsync(Group("1", ownerCount: 1, noCreatedDate: true)));
    }
}
