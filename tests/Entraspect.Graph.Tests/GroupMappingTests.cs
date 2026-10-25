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

        var result = GraphDirectoryReader.MapGroup(group, hasMembers: true, ownerCount: 0);

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

        var result = GraphDirectoryReader.MapGroup(group, hasMembers: false, ownerCount: 1);

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

        Assert.Null(GraphDirectoryReader.MapGroup(group, hasMembers: true, ownerCount: 0).HasOwners);
    }

    [Fact]
    public void MapGroup_synced_security_group_with_no_owners_has_no_owners_in_entra()
    {
        var group = new Group { GroupTypes = [], SecurityEnabled = true, MailEnabled = false, OnPremisesSyncEnabled = true };

        var result = GraphDirectoryReader.MapGroup(group, hasMembers: true, ownerCount: 0);

        Assert.True(result.IsSyncedFromOnPremises);
        Assert.False(result.HasOwners);
    }

    [Theory]
    [InlineData(new string[0], true, true)]
    [InlineData(new string[0], false, true)]
    [InlineData(new[] { "Unified" }, false, true)]
    public void MapGroup_owners_are_unknown_for_other_synced_groups(string[] groupTypes, bool securityEnabled, bool mailEnabled)
    {
        var group = new Group
        {
            GroupTypes = [.. groupTypes],
            SecurityEnabled = securityEnabled,
            MailEnabled = mailEnabled,
            OnPremisesSyncEnabled = true,
        };

        var result = GraphDirectoryReader.MapGroup(group, hasMembers: true, ownerCount: 0);

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

        Assert.True(GraphDirectoryReader.MapGroup(group, hasMembers: true, ownerCount: 1).HasOwners);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(20, 20)]
    public void MapGroup_keeps_the_owner_count_up_to_the_expand_limit(int owners, int expected)
    {
        var group = new Group { GroupTypes = [], SecurityEnabled = true, MailEnabled = false };

        Assert.Equal(expected, GraphDirectoryReader.MapGroup(group, hasMembers: true, ownerCount: owners).OwnerCount);
    }

    [Fact]
    public void MapGroup_counts_owners_graph_returns_where_documented_unavailable()
    {
        var group = new Group { GroupTypes = [], SecurityEnabled = false, MailEnabled = true };

        Assert.Equal(3, GraphDirectoryReader.MapGroup(group, hasMembers: true, ownerCount: 3).OwnerCount);
        Assert.Null(GraphDirectoryReader.MapGroup(group, hasMembers: true, ownerCount: 0).OwnerCount);
    }

    [Fact]
    public void MapGroup_source_of_authority_converted_group_is_checked_like_a_cloud_group()
    {
        // After conversion to cloud management, onPremisesSyncEnabled is null.
        var group = new Group { GroupTypes = [], SecurityEnabled = true, MailEnabled = false, OnPremisesSyncEnabled = null };

        var result = GraphDirectoryReader.MapGroup(group, hasMembers: true, ownerCount: 0);

        Assert.False(result.IsSyncedFromOnPremises);
        Assert.False(result.HasOwners);
    }

    [Fact]
    public void MapGroup_no_longer_synced_group_is_cloud_managed()
    {
        // onPremisesSyncEnabled false: was synced once, now managed in the cloud.
        var group = new Group { GroupTypes = [], SecurityEnabled = true, MailEnabled = false, OnPremisesSyncEnabled = false };

        var result = GraphDirectoryReader.MapGroup(group, hasMembers: true, ownerCount: 0);

        Assert.False(result.IsSyncedFromOnPremises);
        Assert.False(result.HasOwners);
    }

    [Fact]
    public void MapGroup_with_null_fields_uses_empty_values()
    {
        var result = GraphDirectoryReader.MapGroup(new Group(), hasMembers: false, ownerCount: 0);

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

        var result = GraphDirectoryReader.MapGroup(group, hasMembers: true, ownerCount: 1);

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

        var result = GraphDirectoryReader.MapGroup(group, hasMembers: true, ownerCount: 1);

        Assert.True(result.IsSyncedFromOnPremises);
        Assert.Equal(new[] { "Mail", "ProxyAddresses" }, result.SyncErrors.Select(e => e.PropertyCausingError));
        Assert.Equal(1, result.OwnerCount);
    }

    [Fact]
    public void MapGroup_paused_state_on_an_assigned_group_is_not_paused()
    {
        var group = new Group { GroupTypes = [], SecurityEnabled = true, MembershipRuleProcessingState = "Paused" };

        Assert.False(GraphDirectoryReader.MapGroup(group, hasMembers: true, ownerCount: 1).IsDynamicProcessingPaused);
    }
}
