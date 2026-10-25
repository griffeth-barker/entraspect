using Entraspect.Core;
using Microsoft.Graph.Models;

namespace Entraspect.Graph.Tests;

// Mapping tests only: they build Graph SDK objects in memory and never create a client or credential.
public sealed class GroupMappingTests
{
    private static readonly DateTimeOffset Created = new(2025, 3, 1, 9, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(new[] { "Unified" }, false, true, GroupKind.Microsoft365)]
    [InlineData(new[] { "Unified", "DynamicMembership" }, false, true, GroupKind.Microsoft365)]
    [InlineData(new[] { "unified" }, true, true, GroupKind.Microsoft365)]
    [InlineData(new string[0], true, true, GroupKind.MailEnabledSecurity)]
    [InlineData(new string[0], true, false, GroupKind.Security)]
    [InlineData(new[] { "DynamicMembership" }, true, false, GroupKind.Security)]
    [InlineData(new string[0], false, true, GroupKind.Distribution)]
    public void MapGroupKind_follows_group_types_then_security_and_mail_flags(
        string[] groupTypes, bool securityEnabled, bool mailEnabled, GroupKind expected)
    {
        Assert.Equal(expected, GraphDirectoryReader.MapGroupKind(groupTypes, securityEnabled, mailEnabled));
    }

    [Fact]
    public void MapGroupKind_with_missing_values_is_security()
    {
        Assert.Equal(GroupKind.Security, GraphDirectoryReader.MapGroupKind(null, null, null));
    }

    [Fact]
    public void MapGroup_maps_a_security_group()
    {
        var group = new Group
        {
            Id = "group-1",
            DisplayName = "Finance readers",
            GroupTypes = [],
            SecurityEnabled = true,
            MailEnabled = false,
            OnPremisesSyncEnabled = null,
            IsAssignableToRole = true,
            CreatedDateTime = Created,
        };

        var result = GraphDirectoryReader.MapGroup(group, hasMembers: true, hasOwners: false);

        Assert.Equal("group-1", result.Id);
        Assert.Equal("Finance readers", result.DisplayName);
        Assert.Equal(GroupKind.Security, result.Kind);
        Assert.False(result.IsDynamic);
        Assert.False(result.IsSyncedFromOnPremises);
        Assert.True(result.IsRoleAssignable);
        Assert.Equal(Created, result.CreatedDateTime);
        Assert.True(result.HasMembers);
        Assert.False(result.HasOwners);
    }

    [Fact]
    public void MapGroup_maps_a_dynamic_microsoft_365_group()
    {
        var group = new Group
        {
            Id = "group-2",
            GroupTypes = ["Unified", "DynamicMembership"],
            SecurityEnabled = false,
            MailEnabled = true,
        };

        var result = GraphDirectoryReader.MapGroup(group, hasMembers: false, hasOwners: true);

        Assert.Equal(GroupKind.Microsoft365, result.Kind);
        Assert.True(result.IsDynamic);
        Assert.False(result.HasMembers);
        Assert.True(result.HasOwners);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void MapGroup_owners_are_unknown_for_distribution_and_mail_enabled_security_groups(
        bool securityEnabled, bool mailEnabled)
    {
        var group = new Group { GroupTypes = [], SecurityEnabled = securityEnabled, MailEnabled = mailEnabled };

        Assert.Null(GraphDirectoryReader.MapGroup(group, hasMembers: true, hasOwners: false).HasOwners);
    }

    [Theory]
    [InlineData(new string[0], true, false)]
    [InlineData(new[] { "Unified" }, false, true)]
    public void MapGroup_owners_are_unknown_for_synced_groups(string[] groupTypes, bool securityEnabled, bool mailEnabled)
    {
        var group = new Group
        {
            GroupTypes = [.. groupTypes],
            SecurityEnabled = securityEnabled,
            MailEnabled = mailEnabled,
            OnPremisesSyncEnabled = true,
        };

        var result = GraphDirectoryReader.MapGroup(group, hasMembers: true, hasOwners: false);

        Assert.True(result.IsSyncedFromOnPremises);
        Assert.Null(result.HasOwners);
    }

    [Theory]
    [InlineData(new string[0], true, false, true)]
    [InlineData(new string[0], true, true, false)]
    [InlineData(new string[0], false, true, false)]
    public void MapGroup_owners_graph_returns_are_counted_even_where_documented_unavailable(
        string[] groupTypes, bool securityEnabled, bool mailEnabled, bool synced)
    {
        var group = new Group
        {
            GroupTypes = [.. groupTypes],
            SecurityEnabled = securityEnabled,
            MailEnabled = mailEnabled,
            OnPremisesSyncEnabled = synced,
        };

        Assert.True(GraphDirectoryReader.MapGroup(group, hasMembers: true, hasOwners: true).HasOwners);
    }

    [Fact]
    public void MapGroup_source_of_authority_converted_group_is_checked_like_a_cloud_group()
    {
        // After conversion to cloud management, onPremisesSyncEnabled is null.
        var group = new Group { GroupTypes = [], SecurityEnabled = true, MailEnabled = false, OnPremisesSyncEnabled = null };

        var result = GraphDirectoryReader.MapGroup(group, hasMembers: true, hasOwners: false);

        Assert.False(result.IsSyncedFromOnPremises);
        Assert.False(result.HasOwners);
    }

    [Fact]
    public void MapGroup_no_longer_synced_group_is_cloud_managed()
    {
        // onPremisesSyncEnabled false: was synced once, now managed in the cloud.
        var group = new Group { GroupTypes = [], SecurityEnabled = true, MailEnabled = false, OnPremisesSyncEnabled = false };

        var result = GraphDirectoryReader.MapGroup(group, hasMembers: true, hasOwners: false);

        Assert.False(result.IsSyncedFromOnPremises);
        Assert.False(result.HasOwners);
    }

    [Fact]
    public void MapGroup_with_null_fields_uses_empty_values()
    {
        var result = GraphDirectoryReader.MapGroup(new Group(), hasMembers: false, hasOwners: false);

        Assert.Equal(string.Empty, result.Id);
        Assert.Equal(string.Empty, result.DisplayName);
        Assert.Equal(GroupKind.Security, result.Kind);
        Assert.False(result.IsDynamic);
        Assert.False(result.IsRoleAssignable);
        Assert.Null(result.CreatedDateTime);
    }
}
