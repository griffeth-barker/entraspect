using Entraspect.Core.Checks;
using Microsoft.Extensions.Time.Testing;
using static Entraspect.Core.Tests.Checks.GroupTestData;

namespace Entraspect.Core.Tests.Checks;

public sealed class GroupNoMembersCheckTests
{
    private readonly FakeTimeProvider _time = new(Now);

    private Task<IReadOnlyList<Finding>> RunAsync(params DirectoryGroup[] groups) =>
        RunAsync(new CheckOptions(), groups);

    private async Task<IReadOnlyList<Finding>> RunAsync(CheckOptions options, params DirectoryGroup[] groups)
    {
        var check = new GroupNoMembersCheck(_time);
        return await check.RunAsync(new FakeDirectoryReader(groups), options);
    }

    [Fact]
    public async Task Group_with_members_has_no_finding()
    {
        Assert.Empty(await RunAsync(Group("1", hasMembers: true)));
    }

    [Fact]
    public async Task Empty_assigned_group_is_a_warning_about_the_group()
    {
        var f = Assert.Single(await RunAsync(Group("1", hasMembers: false)));

        Assert.Equal("group-no-members", f.CheckId);
        Assert.Equal(Severity.Warning, f.Severity);
        Assert.Equal("Group", f.ObjectType);
        Assert.Equal("1", f.ObjectId);
        Assert.Equal("Group 1", f.ObjectDisplayName);
        Assert.Equal("Security group has no members.", f.Message);
        Assert.Null(f.DueDate);
        Assert.Null(f.SubObjectType);
        Assert.Null(f.SubObjectDisplayName);
    }

    [Theory]
    [InlineData(GroupKind.Microsoft365, "Microsoft 365 group has no members.")]
    [InlineData(GroupKind.Security, "Security group has no members.")]
    [InlineData(GroupKind.MailEnabledSecurity, "Mail-enabled security group has no members.")]
    [InlineData(GroupKind.Distribution, "Distribution group has no members.")]
    public async Task Empty_assigned_group_of_each_kind_is_a_warning(GroupKind kind, string message)
    {
        var f = Assert.Single(await RunAsync(Group("1", kind, hasMembers: false)));

        Assert.Equal(Severity.Warning, f.Severity);
        Assert.Equal(message, f.Message);
    }

    [Theory]
    [InlineData(GroupKind.Microsoft365, "Dynamic Microsoft 365 group has no members; its rule matches no one.")]
    [InlineData(GroupKind.Security, "Dynamic security group has no members; its rule matches no one.")]
    public async Task Empty_dynamic_group_is_info(GroupKind kind, string message)
    {
        var f = Assert.Single(await RunAsync(Group("1", kind, isDynamic: true, hasMembers: false)));

        Assert.Equal(Severity.Info, f.Severity);
        Assert.Equal(message, f.Message);
    }

    [Fact]
    public async Task Dynamic_group_with_members_has_no_finding()
    {
        Assert.Empty(await RunAsync(Group("1", isDynamic: true, hasMembers: true)));
    }

    [Theory]
    [InlineData(GroupKind.Security, "Synced security group")]
    [InlineData(GroupKind.MailEnabledSecurity, "Synced mail-enabled security group")]
    [InlineData(GroupKind.Distribution, "Synced distribution group")]
    [InlineData(GroupKind.Microsoft365, "Synced Microsoft 365 group")]
    public async Task Empty_synced_group_is_a_warning_that_mentions_the_sync_scope(GroupKind kind, string start)
    {
        var f = Assert.Single(await RunAsync(Group("1", kind, isSynced: true, hasMembers: false, hasOwners: null)));

        Assert.Equal(Severity.Warning, f.Severity);
        Assert.Equal(
            $"{start} has no members in Entra ID. Members outside the sync scope aren't synced, so cloud policies " +
            "and assignments that use it reach no one.",
            f.Message);
    }

    [Fact]
    public async Task Synced_group_with_members_has_no_finding()
    {
        Assert.Empty(await RunAsync(Group("1", isSynced: true, hasMembers: true, hasOwners: null)));
    }

    [Fact]
    public async Task Group_created_exactly_grace_days_ago_is_checked()
    {
        // Late on the UTC day 7 days ago: fewer than 7 elapsed days, but 7 calendar days.
        var created = new DateTimeOffset(2026, 1, 8, 23, 0, 0, TimeSpan.Zero);

        Assert.Single(await RunAsync(Group("1", hasMembers: false, created: created)));
    }

    [Fact]
    public async Task Group_created_one_day_inside_the_grace_period_is_skipped()
    {
        var created = new DateTimeOffset(2026, 1, 9, 0, 0, 0, TimeSpan.Zero);

        Assert.Empty(await RunAsync(Group("1", hasMembers: false, created: created)));
    }

    [Fact]
    public async Task Grace_period_uses_utc_calendar_days()
    {
        // 2026-01-09 01:00 at UTC+3 is 2026-01-08 22:00 UTC: 7 UTC calendar days ago.
        var created = new DateTimeOffset(2026, 1, 9, 1, 0, 0, TimeSpan.FromHours(3));

        Assert.Single(await RunAsync(Group("1", hasMembers: false, created: created)));
    }

    [Fact]
    public async Task Zero_grace_days_checks_groups_created_today()
    {
        var options = new CheckOptions { NewGroupGraceDays = 0 };

        Assert.Single(await RunAsync(options, Group("1", hasMembers: false, created: Now)));
    }

    [Fact]
    public async Task Group_without_created_date_is_checked()
    {
        Assert.Single(await RunAsync(Group("1", hasMembers: false, noCreatedDate: true)));
    }

    [Fact]
    public async Task Each_empty_group_has_its_own_finding()
    {
        var findings = await RunAsync(
            Group("1", hasMembers: false),
            Group("2", hasMembers: true),
            Group("3", hasMembers: false));

        Assert.Equal(new[] { "1", "3" }, findings.Select(f => f.ObjectId));
    }
}
