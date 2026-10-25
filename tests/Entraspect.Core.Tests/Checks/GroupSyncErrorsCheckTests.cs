using Entraspect.Core.Checks;
using static Entraspect.Core.Tests.Checks.GroupTestData;

namespace Entraspect.Core.Tests.Checks;

public sealed class GroupSyncErrorsCheckTests
{
    private static readonly DateTimeOffset Occurred = new(2026, 1, 10, 8, 15, 0, TimeSpan.Zero);

    private static Task<IReadOnlyList<Finding>> RunAsync(params DirectoryGroup[] groups) =>
        new GroupSyncErrorsCheck().RunAsync(new FakeDirectoryReader(groups), new CheckOptions());

    private static GroupSyncError Error(
        string category = "PropertyConflict",
        string property = "ProxyAddresses",
        string value = "smtp:sales@contoso.com",
        DateTimeOffset? occurred = null) =>
        new()
        {
            Category = category,
            PropertyCausingError = property,
            Value = value,
            OccurredDateTime = occurred ?? Occurred,
        };

    [Fact]
    public async Task Sync_error_on_a_synced_group_is_a_warning_with_its_details()
    {
        var f = Assert.Single(await RunAsync(Group("1", isSynced: true, syncErrors: [Error()])));

        Assert.Equal("group-sync-errors", f.CheckId);
        Assert.Equal(Severity.Warning, f.Severity);
        Assert.Equal("Group", f.ObjectType);
        Assert.Equal("1", f.ObjectId);
        Assert.Equal("Group 1", f.ObjectDisplayName);
        Assert.Equal("On-premises sync error on ProxyAddresses: smtp:sales@contoso.com.", f.Message);
        Assert.Equal("PropertyConflict", f.SubObjectType);
        Assert.Equal("ProxyAddresses", f.SubObjectDisplayName);
        Assert.Equal(Occurred, f.DueDate);
    }

    [Fact]
    public async Task Each_sync_error_is_its_own_finding()
    {
        var findings = await RunAsync(
            Group("1", isSynced: true, syncErrors:
            [
                Error(property: "ProxyAddresses"),
                Error(property: "Mail", value: "sales@contoso.com"),
            ]),
            Group("2", isSynced: true, syncErrors: [Error()]));

        Assert.Equal(new[] { "1", "1", "2" }, findings.Select(f => f.ObjectId));
        Assert.Equal(new[] { "ProxyAddresses", "Mail", "ProxyAddresses" }, findings.Select(f => f.SubObjectDisplayName));
    }

    [Fact]
    public async Task Group_without_sync_errors_has_no_finding()
    {
        Assert.Empty(await RunAsync(Group("1", isSynced: true), Group("2")));
    }

    [Fact]
    public async Task Sync_error_on_a_group_no_longer_synced_is_still_reported()
    {
        Assert.Single(await RunAsync(Group("1", isSynced: false, syncErrors: [Error()])));
    }

    [Theory]
    [InlineData("ProxyAddresses", "smtp:a@contoso.com", "On-premises sync error on ProxyAddresses: smtp:a@contoso.com.")]
    [InlineData("ProxyAddresses", "", "On-premises sync error on ProxyAddresses.")]
    [InlineData("", "smtp:a@contoso.com", "On-premises sync error: smtp:a@contoso.com.")]
    [InlineData("", "", "On-premises sync error.")]
    [InlineData(" ", " ", "On-premises sync error.")]
    public void Message_leaves_out_the_parts_that_are_empty(string property, string value, string expected)
    {
        Assert.Equal(expected, GroupSyncErrorsCheck.Message(Error(property: property, value: value)));
    }

    [Fact]
    public async Task Empty_error_fields_leave_their_columns_empty()
    {
        var error = new GroupSyncError();

        var f = Assert.Single(await RunAsync(Group("1", isSynced: true, syncErrors: [error])));

        Assert.Equal("On-premises sync error.", f.Message);
        Assert.Null(f.SubObjectType);
        Assert.Null(f.SubObjectDisplayName);
        Assert.Null(f.DueDate);
    }
}
