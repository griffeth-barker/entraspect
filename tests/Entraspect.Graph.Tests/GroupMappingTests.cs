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

        var result = GraphDirectoryReader.MapGroup(group);

        Assert.Equal("group-1", result.Id);
        Assert.Equal("Finance readers", result.DisplayName);
        Assert.Equal(GroupKind.Security, result.Kind);
        Assert.False(result.IsDynamic);
        Assert.False(result.IsSyncedFromOnPremises);
        Assert.True(result.IsRoleAssignable);
        Assert.Equal(Created, result.CreatedDateTime);
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

        var result = GraphDirectoryReader.MapGroup(group);

        Assert.Equal(GroupKind.Microsoft365, result.Kind);
        Assert.True(result.IsDynamic);
    }

    /// <summary>A group as the owners listing returns it, with <paramref name="owners"/> expanded owners.</summary>
    private static Group WithOwners(Group group, int owners)
    {
        group.Owners = Enumerable.Range(1, owners).Select(i => new DirectoryObject { Id = $"owner-{i}" }).ToList();
        return group;
    }

    [Fact]
    public void MapGroupCounts_keeps_the_id_and_whether_the_group_has_members()
    {
        var group = WithOwners(new Group { Id = "group-1", GroupTypes = [], SecurityEnabled = true }, 1);

        var withMembers = GraphDirectoryReader.MapGroupCounts(group, hasMembers: true);
        var withoutMembers = GraphDirectoryReader.MapGroupCounts(group, hasMembers: false);

        Assert.Equal("group-1", withMembers.GroupId);
        Assert.True(withMembers.HasMembers);
        Assert.False(withoutMembers.HasMembers);
    }

    [Fact]
    public void MapGroupCounts_security_group_with_no_owners_has_no_owners()
    {
        var group = new Group { GroupTypes = [], SecurityEnabled = true, MailEnabled = false };

        Assert.False(GraphDirectoryReader.MapGroupCounts(group, hasMembers: true).HasOwners);
        Assert.False(GraphDirectoryReader.MapGroupCounts(WithOwners(group, 0), hasMembers: true).HasOwners);
    }

    [Fact]
    public void MapGroupCounts_microsoft_365_group_with_an_owner_has_owners()
    {
        var group = WithOwners(new Group { GroupTypes = ["Unified"], SecurityEnabled = false, MailEnabled = true }, 1);

        Assert.True(GraphDirectoryReader.MapGroupCounts(group, hasMembers: true).HasOwners);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void MapGroupCounts_owners_are_unknown_for_distribution_and_mail_enabled_security_groups(
        bool securityEnabled, bool mailEnabled)
    {
        var group = new Group { GroupTypes = [], SecurityEnabled = securityEnabled, MailEnabled = mailEnabled };

        Assert.Null(GraphDirectoryReader.MapGroupCounts(group, hasMembers: true).HasOwners);
    }

    [Fact]
    public void MapGroupCounts_synced_security_group_with_no_owners_has_no_owners_in_entra()
    {
        var group = new Group { GroupTypes = [], SecurityEnabled = true, MailEnabled = false, OnPremisesSyncEnabled = true };

        Assert.False(GraphDirectoryReader.MapGroupCounts(group, hasMembers: true).HasOwners);
    }

    [Theory]
    [InlineData(new string[0], true, true)]
    [InlineData(new string[0], false, true)]
    [InlineData(new[] { "Unified" }, false, true)]
    public void MapGroupCounts_owners_are_unknown_for_other_synced_groups(
        string[] groupTypes, bool securityEnabled, bool mailEnabled)
    {
        var group = new Group
        {
            GroupTypes = [.. groupTypes],
            SecurityEnabled = securityEnabled,
            MailEnabled = mailEnabled,
            OnPremisesSyncEnabled = true,
        };

        Assert.Null(GraphDirectoryReader.MapGroupCounts(group, hasMembers: true).HasOwners);
    }

    [Theory]
    [InlineData(new string[0], true, false, true)]
    [InlineData(new string[0], true, true, false)]
    [InlineData(new string[0], false, true, false)]
    public void MapGroupCounts_owners_graph_returns_are_counted_even_where_documented_unavailable(
        string[] groupTypes, bool securityEnabled, bool mailEnabled, bool synced)
    {
        var group = WithOwners(
            new Group
            {
                GroupTypes = [.. groupTypes],
                SecurityEnabled = securityEnabled,
                MailEnabled = mailEnabled,
                OnPremisesSyncEnabled = synced,
            },
            1);

        Assert.True(GraphDirectoryReader.MapGroupCounts(group, hasMembers: true).HasOwners);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(20, 20)]
    public void MapGroupCounts_keeps_the_owner_count_up_to_the_expand_limit(int owners, int expected)
    {
        var group = WithOwners(new Group { GroupTypes = [], SecurityEnabled = true, MailEnabled = false }, owners);

        Assert.Equal(expected, GraphDirectoryReader.MapGroupCounts(group, hasMembers: true).OwnerCount);
    }

    [Fact]
    public void MapGroupCounts_counts_owners_graph_returns_where_documented_unavailable()
    {
        Group Distribution(int owners) =>
            WithOwners(new Group { GroupTypes = [], SecurityEnabled = false, MailEnabled = true }, owners);

        Assert.Equal(3, GraphDirectoryReader.MapGroupCounts(Distribution(3), hasMembers: true).OwnerCount);
        Assert.Null(GraphDirectoryReader.MapGroupCounts(Distribution(0), hasMembers: true).OwnerCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public void MapGroupCounts_cloud_managed_group_is_counted_like_a_cloud_group(bool? onPremisesSyncEnabled)
    {
        // null after conversion to cloud management (source of authority); false when it was synced once and is
        // now managed in the cloud.
        var group = new Group
        {
            GroupTypes = [],
            SecurityEnabled = true,
            MailEnabled = false,
            OnPremisesSyncEnabled = onPremisesSyncEnabled,
        };

        Assert.False(GraphDirectoryReader.MapGroupCounts(group, hasMembers: true).HasOwners);
    }

    [Fact]
    public void MapGroup_synced_and_cloud_managed_groups()
    {
        var synced = new Group { GroupTypes = [], SecurityEnabled = true, OnPremisesSyncEnabled = true };
        var converted = new Group { GroupTypes = [], SecurityEnabled = true, OnPremisesSyncEnabled = null };
        var noLongerSynced = new Group { GroupTypes = [], SecurityEnabled = true, OnPremisesSyncEnabled = false };

        Assert.True(GraphDirectoryReader.MapGroup(synced).IsSyncedFromOnPremises);
        Assert.False(GraphDirectoryReader.MapGroup(converted).IsSyncedFromOnPremises);
        Assert.False(GraphDirectoryReader.MapGroup(noLongerSynced).IsSyncedFromOnPremises);
    }

    [Fact]
    public void MapGroup_with_null_fields_uses_empty_values()
    {
        var result = GraphDirectoryReader.MapGroup(new Group());

        Assert.Equal(string.Empty, result.Id);
        Assert.Equal(string.Empty, result.DisplayName);
        Assert.Equal(GroupKind.Security, result.Kind);
        Assert.False(result.IsDynamic);
        Assert.False(result.IsRoleAssignable);
        Assert.Null(result.CreatedDateTime);
        Assert.Null(result.Visibility);
        Assert.False(result.IsDynamicProcessingPaused);
        Assert.Empty(result.SyncErrors);
    }

    [Theory]
    [InlineData("Public", GroupVisibility.Public)]
    [InlineData("public", GroupVisibility.Public)]
    [InlineData("Private", GroupVisibility.Private)]
    [InlineData("HiddenMembership", GroupVisibility.HiddenMembership)]
    [InlineData("hiddenmembership", GroupVisibility.HiddenMembership)]
    [InlineData("Restricted", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void MapGroupVisibility_knows_three_values_and_ignores_case(string? visibility, GroupVisibility? expected)
    {
        Assert.Equal(expected, GraphDirectoryReader.MapGroupVisibility(visibility));
    }

    [Theory]
    [InlineData(true, "Paused", true)]
    [InlineData(true, "paused", true)]
    [InlineData(true, "On", false)]
    [InlineData(true, "Evaluating", false)]
    [InlineData(true, null, false)]
    [InlineData(false, "Paused", false)]
    public void IsProcessingPaused_only_for_dynamic_groups_whose_state_is_paused(
        bool isDynamic, string? state, bool expected)
    {
        Assert.Equal(expected, GraphDirectoryReader.IsProcessingPaused(isDynamic, state));
    }

    [Fact]
    public void MapGroupSyncErrors_keeps_each_error()
    {
        var occurred = new DateTimeOffset(2026, 1, 10, 8, 15, 0, TimeSpan.Zero);

        var result = GraphDirectoryReader.MapGroupSyncErrors(
        [
            new OnPremisesProvisioningError
            {
                Category = "PropertyConflict",
                PropertyCausingError = "ProxyAddresses",
                Value = "smtp:sales@contoso.com",
                OccurredDateTime = occurred,
            },
            new OnPremisesProvisioningError(),
            null,
        ]);

        Assert.Equal(2, result.Count);
        Assert.Equal(
            new GroupSyncError
            {
                Category = "PropertyConflict",
                PropertyCausingError = "ProxyAddresses",
                Value = "smtp:sales@contoso.com",
                OccurredDateTime = occurred,
            },
            result[0]);
        Assert.Equal(new GroupSyncError(), result[1]);
    }

    [Fact]
    public void MapGroupSyncErrors_without_errors_is_empty()
    {
        Assert.Empty(GraphDirectoryReader.MapGroupSyncErrors(null));
        Assert.Empty(GraphDirectoryReader.MapGroupSyncErrors([]));
    }

    [Fact]
    public void MapGroup_maps_visibility_paused_processing_and_sync_errors()
    {
        var group = new Group
        {
            GroupTypes = ["Unified", "DynamicMembership"],
            SecurityEnabled = false,
            MailEnabled = true,
            Visibility = "Public",
            MembershipRuleProcessingState = "Paused",
            OnPremisesProvisioningErrors = [new OnPremisesProvisioningError { PropertyCausingError = "Mail" }],
        };

        var result = GraphDirectoryReader.MapGroup(group);

        Assert.Equal(GroupVisibility.Public, result.Visibility);
        Assert.True(result.IsDynamicProcessingPaused);
        Assert.Equal("Mail", Assert.Single(result.SyncErrors).PropertyCausingError);
    }

    [Fact]
    public void MapGroup_synced_security_group_keeps_its_sync_errors()
    {
        var group = new Group
        {
            GroupTypes = [],
            SecurityEnabled = true,
            MailEnabled = false,
            OnPremisesSyncEnabled = true,
            OnPremisesProvisioningErrors =
            [
                new OnPremisesProvisioningError { Category = "PropertyConflict", PropertyCausingError = "Mail" },
                new OnPremisesProvisioningError { Category = "PropertyConflict", PropertyCausingError = "ProxyAddresses" },
            ],
        };

        var result = GraphDirectoryReader.MapGroup(group);

        Assert.True(result.IsSyncedFromOnPremises);
        Assert.Equal(new[] { "Mail", "ProxyAddresses" }, result.SyncErrors.Select(e => e.PropertyCausingError));
    }

    [Fact]
    public void MapGroup_paused_state_on_an_assigned_group_is_not_paused()
    {
        var group = new Group { GroupTypes = [], SecurityEnabled = true, MembershipRuleProcessingState = "Paused" };

        Assert.False(GraphDirectoryReader.MapGroup(group).IsDynamicProcessingPaused);
    }
}
