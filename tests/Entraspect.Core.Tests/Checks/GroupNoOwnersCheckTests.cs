using Entraspect.Core.Checks;
using Microsoft.Extensions.Time.Testing;
using static Entraspect.Core.Tests.Checks.GroupTestData;

namespace Entraspect.Core.Tests.Checks;

public sealed class GroupNoOwnersCheckTests
{
    private readonly FakeTimeProvider _time = new(Now);

    private Task<IReadOnlyList<Finding>> RunAsync(params DirectoryGroup[] groups) =>
        RunAsync(new CheckOptions(), groups);

    private async Task<IReadOnlyList<Finding>> RunAsync(CheckOptions options, params DirectoryGroup[] groups)
    {
        var check = new GroupNoOwnersCheck(_time);
        return await check.RunAsync(new FakeDirectoryReader(groups), options);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public async Task Group_with_owners_or_unknown_owners_has_no_finding(bool? hasOwners)
    {
        Assert.Empty(await RunAsync(
            Group("1", GroupKind.Microsoft365, hasOwners: hasOwners),
            Group("2", GroupKind.Security, hasOwners: hasOwners)));
    }

    [Fact]
    public async Task Ownerless_microsoft_365_group_is_a_warning_about_the_group()
    {
        var f = Assert.Single(await RunAsync(Group("1", GroupKind.Microsoft365, hasOwners: false)));

        Assert.Equal("group-no-owners", f.CheckId);
        Assert.Equal(Severity.Warning, f.Severity);
        Assert.Equal("Group", f.ObjectType);
        Assert.Equal("1", f.ObjectId);
        Assert.Equal("Group 1", f.ObjectDisplayName);
        Assert.Equal(
            "Microsoft 365 group has no owners. No one can manage its membership, and it can't be renewed when " +
            "group expiration is on.",
            f.Message);
        Assert.Null(f.DueDate);
        Assert.Null(f.SubObjectType);
        Assert.Null(f.SubObjectDisplayName);
    }

    [Fact]
    public async Task Ownerless_security_group_is_info()
    {
        var f = Assert.Single(await RunAsync(Group("1", GroupKind.Security, hasOwners: false)));

        Assert.Equal(Severity.Info, f.Severity);
        Assert.Equal("Security group has no owners.", f.Message);
    }

    [Theory]
    [InlineData(GroupKind.Microsoft365)]
    [InlineData(GroupKind.Security)]
    public async Task Ownerless_dynamic_group_is_reported_like_an_assigned_one(GroupKind kind)
    {
        Assert.Single(await RunAsync(Group("1", kind, isDynamic: true, hasOwners: false)));
    }

    [Theory]
    [InlineData(GroupKind.MailEnabledSecurity)]
    [InlineData(GroupKind.Distribution)]
    public async Task Mail_enabled_security_and_distribution_groups_have_no_finding(GroupKind kind)
    {
        Assert.Empty(await RunAsync(Group("1", kind, hasOwners: null), Group("2", kind, hasOwners: false)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public async Task Synced_group_has_no_finding(bool? hasOwners)
    {
        Assert.Empty(await RunAsync(
            Group("1", GroupKind.Security, isSynced: true, hasOwners: hasOwners),
            Group("2", GroupKind.Microsoft365, isSynced: true, hasOwners: hasOwners)));
    }

    [Fact]
    public async Task Group_created_exactly_grace_days_ago_is_checked()
    {
        var created = new DateTimeOffset(2026, 1, 8, 23, 0, 0, TimeSpan.Zero);

        Assert.Single(await RunAsync(Group("1", hasOwners: false, created: created)));
    }

    [Fact]
    public async Task Group_inside_the_grace_period_is_skipped()
    {
        var created = new DateTimeOffset(2026, 1, 9, 0, 0, 0, TimeSpan.Zero);

        Assert.Empty(await RunAsync(Group("1", hasOwners: false, created: created)));
    }

    [Fact]
    public async Task Custom_grace_days_are_used()
    {
        var options = new CheckOptions { NewGroupGraceDays = 30 };
        var created = Now.AddDays(-20);

        Assert.Empty(await RunAsync(options, Group("1", hasOwners: false, created: created)));
    }

    [Fact]
    public async Task Group_without_created_date_is_checked()
    {
        Assert.Single(await RunAsync(Group("1", hasOwners: false, noCreatedDate: true)));
    }

    [Fact]
    public async Task Group_with_no_members_and_no_owners_has_a_finding_from_each_check()
    {
        var group = Group("1", GroupKind.Microsoft365, hasMembers: false, hasOwners: false);
        var reader = new FakeDirectoryReader([group]);
        var options = new CheckOptions();

        var noOwners = await new GroupNoOwnersCheck(_time).RunAsync(reader, options);
        var noMembers = await new GroupNoMembersCheck(_time).RunAsync(reader, options);

        Assert.Equal("1", Assert.Single(noOwners).ObjectId);
        Assert.Equal("1", Assert.Single(noMembers).ObjectId);
    }
}
