using System.IO;
using System.Text;
using System.Text.Json;
using Entraspect.Core;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Serialization.Json;
using AllowInvitesFrom = Entraspect.Core.AllowInvitesFrom;

namespace Entraspect.Graph.Tests;

// Mapping tests only: they parse sample Graph JSON in memory and never create a client or credential.
public sealed class ExternalIdentitiesMappingTests
{
    // The default cross-tenant access settings from the Graph v1.0 docs example.
    private const string ServiceDefaultsJson = """
        {
          "isServiceDefault": true,
          "inboundTrust": {
            "isMfaAccepted": false,
            "isCompliantDeviceAccepted": false,
            "isHybridAzureADJoinedDeviceAccepted": false
          },
          "automaticUserConsentSettings": { "inboundAllowed": false, "outboundAllowed": false },
          "b2bCollaborationOutbound": {
            "usersAndGroups": { "accessType": "allowed", "targets": [ { "target": "AllUsers", "targetType": "user" } ] },
            "applications": { "accessType": "allowed", "targets": [ { "target": "AllApplications", "targetType": "application" } ] }
          },
          "b2bCollaborationInbound": {
            "usersAndGroups": { "accessType": "allowed", "targets": [ { "target": "AllUsers", "targetType": "user" } ] },
            "applications": { "accessType": "allowed", "targets": [ { "target": "AllApplications", "targetType": "application" } ] }
          },
          "b2bDirectConnectOutbound": {
            "usersAndGroups": { "accessType": "blocked", "targets": [ { "target": "AllUsers", "targetType": "user" } ] },
            "applications": { "accessType": "blocked", "targets": [ { "target": "AllApplications", "targetType": "application" } ] }
          },
          "b2bDirectConnectInbound": {
            "usersAndGroups": { "accessType": "blocked", "targets": [ { "target": "AllUsers", "targetType": "user" } ] },
            "applications": { "accessType": "blocked", "targets": [ { "target": "AllApplications", "targetType": "application" } ] }
          },
          "m365CollaborationInbound": { "users": { "accessType": "blocked", "targets": [] } }
        }
        """;

    [Fact]
    public void Authorization_policy_maps_the_docs_example()
    {
        var settings = MapAuthorizationPolicy("""
            {
              "id": "authorizationPolicy",
              "allowInvitesFrom": "everyone",
              "allowEmailVerifiedUsersToJoinOrganization": false,
              "guestUserRoleId": "10dae51f-b6af-4016-8d66-8c2a99b929b3"
            }
            """);

        Assert.Equal("10dae51f-b6af-4016-8d66-8c2a99b929b3", settings.GuestUserRoleId);
        Assert.Equal(AllowInvitesFrom.Everyone, settings.AllowInvitesFrom);
        Assert.Equal("everyone", settings.AllowInvitesFromRaw);
        Assert.False(settings.AllowEmailVerifiedUsersToJoinOrganization);
    }

    [Theory]
    [InlineData("none", AllowInvitesFrom.None)]
    [InlineData("adminsAndGuestInviters", AllowInvitesFrom.AdminsAndGuestInviters)]
    [InlineData("adminsGuestInvitersAndAllMembers", AllowInvitesFrom.AdminsGuestInvitersAndAllMembers)]
    [InlineData("everyone", AllowInvitesFrom.Everyone)]
    [InlineData("EVERYONE", AllowInvitesFrom.Everyone)]
    [InlineData("unknownFutureValue", AllowInvitesFrom.Unknown)]
    [InlineData("someNewValue", AllowInvitesFrom.Unknown)]
    [InlineData("", AllowInvitesFrom.Unknown)]
    [InlineData(null, AllowInvitesFrom.Unknown)]
    public void Allow_invites_from_maps_every_value(string? value, AllowInvitesFrom expected)
    {
        Assert.Equal(expected, GraphDirectoryReader.MapAllowInvitesFrom(value));
    }

    [Fact]
    public void Unknown_values_keep_the_raw_text()
    {
        var settings = MapAuthorizationPolicy("""
            { "allowInvitesFrom": "someNewValue", "guestUserRoleId": "not-a-guid" }
            """);

        Assert.Equal(AllowInvitesFrom.Unknown, settings.AllowInvitesFrom);
        Assert.Equal("someNewValue", settings.AllowInvitesFromRaw);
        Assert.Equal("not-a-guid", settings.GuestUserRoleId);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "allowInvitesFrom": null, "guestUserRoleId": null, "allowEmailVerifiedUsersToJoinOrganization": null }""")]
    [InlineData("""{ "allowInvitesFrom": 3, "guestUserRoleId": true, "allowEmailVerifiedUsersToJoinOrganization": "yes" }""")]
    [InlineData("[]")]
    [InlineData("null")]
    public void Missing_or_unexpected_authorization_policy_values_never_throw(string json)
    {
        var settings = MapAuthorizationPolicy(json);

        Assert.Equal(string.Empty, settings.GuestUserRoleId);
        Assert.Equal(AllowInvitesFrom.Unknown, settings.AllowInvitesFrom);
        Assert.Null(settings.AllowInvitesFromRaw);
        Assert.Null(settings.AllowEmailVerifiedUsersToJoinOrganization);
    }

    [Fact]
    public void Email_verified_join_maps_true()
    {
        Assert.True(MapAuthorizationPolicy("""{ "allowEmailVerifiedUsersToJoinOrganization": true }""")
            .AllowEmailVerifiedUsersToJoinOrganization);
    }

    [Fact]
    public async Task Service_defaults_map_the_docs_example()
    {
        var defaults = GraphDirectoryReader.MapCrossTenantAccessDefaults(
            await ParseAsync(ServiceDefaultsJson, CrossTenantAccessPolicyConfigurationDefault.CreateFromDiscriminatorValue));

        Assert.Null(defaults.TenantId);
        Assert.True(defaults.IsServiceDefault);
        Assert.False(defaults.InboundTrust.IsMfaAccepted);
        Assert.False(defaults.InboundTrust.IsCompliantDeviceAccepted);
        Assert.False(defaults.InboundTrust.IsHybridJoinedDeviceAccepted);
        Assert.False(defaults.AutomaticRedemptionInbound);
        Assert.False(defaults.AutomaticRedemptionOutbound);
        Assert.True(defaults.B2bCollaborationInbound!.IsOpenToAll);
        Assert.True(defaults.B2bCollaborationOutbound!.IsOpenToAll);
        Assert.Equal(
            new CrossTenantAccessTarget(CrossTenantAccessType.Blocked, IncludesAll: true),
            defaults.B2bDirectConnectInbound!.Users);
        Assert.False(defaults.B2bDirectConnectInbound.IsAllowedForAny);
        Assert.False(defaults.B2bDirectConnectOutbound!.IsAllowedForAny);
    }

    [Fact]
    public void Missing_defaults_map_to_unset_settings()
    {
        var defaults = GraphDirectoryReader.MapCrossTenantAccessDefaults(null);

        Assert.Null(defaults.IsServiceDefault);
        Assert.Null(defaults.InboundTrust.IsMfaAccepted);
        Assert.Null(defaults.AutomaticRedemptionInbound);
        Assert.Null(defaults.B2bCollaborationInbound);
        Assert.Null(defaults.B2bDirectConnectOutbound);
    }

    [Fact]
    public async Task Unknown_access_type_and_missing_targets_never_throw()
    {
        var defaults = GraphDirectoryReader.MapCrossTenantAccessDefaults(await ParseAsync("""
            {
              "b2bDirectConnectInbound": {
                "usersAndGroups": { "accessType": "someNewType", "targets": [ { "target": "AllUsers", "targetType": "user" } ] },
                "applications": { "accessType": "allowed" }
              },
              "b2bDirectConnectOutbound": {
                "usersAndGroups": { "targets": null },
                "applications": { "accessType": "allowed", "targets": [ null, { "targetType": "application" } ] }
              },
              "b2bCollaborationInbound": { "applications": { "accessType": "allowed", "targets": [ { "target": "AllApplications" } ] } }
            }
            """, CrossTenantAccessPolicyConfigurationDefault.CreateFromDiscriminatorValue));

        var inbound = defaults.B2bDirectConnectInbound!;
        Assert.Equal(new CrossTenantAccessTarget(CrossTenantAccessType.Unknown, IncludesAll: true), inbound.Users);
        Assert.Equal(new CrossTenantAccessTarget(CrossTenantAccessType.Allowed, IncludesAll: false), inbound.Applications);
        Assert.False(inbound.IsAllowedForAny);

        var outbound = defaults.B2bDirectConnectOutbound!;
        Assert.Equal(new CrossTenantAccessTarget(CrossTenantAccessType.Unknown, IncludesAll: false), outbound.Users);
        Assert.Equal(new CrossTenantAccessTarget(CrossTenantAccessType.Allowed, IncludesAll: false), outbound.Applications);

        // usersAndGroups missing: the side is unknown, so it is neither open to all nor allowed for any.
        Assert.Null(defaults.B2bCollaborationInbound!.Users);
        Assert.False(defaults.B2bCollaborationInbound.IsOpenToAll);
        Assert.False(defaults.B2bCollaborationInbound.IsAllowedForAny);
    }

    [Theory]
    [InlineData("AllApplications", true)]
    [InlineData("allapplications", true)]
    [InlineData("Office365", false)]
    [InlineData("00000003-0000-0000-c000-000000000000", false)]
    public void Only_the_all_applications_target_includes_all(string target, bool includesAll)
    {
        var mapped = GraphDirectoryReader.MapAccessTarget(
            new CrossTenantAccessPolicyTargetConfiguration
            {
                AccessType = CrossTenantAccessPolicyTargetConfigurationAccessType.Allowed,
                Targets = [new CrossTenantAccessPolicyTarget { Target = target }],
            },
            GraphDirectoryReader.AllApplicationsTarget);

        Assert.Equal(new CrossTenantAccessTarget(CrossTenantAccessType.Allowed, includesAll), mapped);
    }

    [Fact]
    public async Task Partners_page_maps_the_docs_example_and_keeps_the_next_link()
    {
        var page = await ParseAsync("""
            {
              "@odata.nextLink": "https://graph.microsoft.com/v1.0/policies/crossTenantAccessPolicy/partners?$skiptoken=abc",
              "value": [
                {
                  "tenantId": "123f4846-ba00-4fd7-ba43-dac1f8f63013",
                  "inboundTrust": null,
                  "isInMultiTenantOrganization": false,
                  "automaticUserConsentSettings": { "inboundAllowed": null, "outboundAllowed": null },
                  "b2bCollaborationInbound": null,
                  "b2bCollaborationOutbound": null,
                  "b2bDirectConnectOutbound": null,
                  "b2bDirectConnectInbound": {
                    "usersAndGroups": { "accessType": "allowed", "targets": [ { "target": "AllUsers", "targetType": "user" } ] },
                    "applications": { "accessType": "allowed", "targets": [ { "target": "Office365", "targetType": "application" } ] }
                  }
                },
                {
                  "tenantId": "00000000-0000-0000-0000-00000000000b",
                  "isServiceProvider": true,
                  "inboundTrust": { "isMfaAccepted": true, "isCompliantDeviceAccepted": null, "isHybridAzureADJoinedDeviceAccepted": false },
                  "automaticUserConsentSettings": { "inboundAllowed": true, "outboundAllowed": false }
                }
              ]
            }
            """, CrossTenantAccessPolicyConfigurationPartnerCollectionResponse.CreateFromDiscriminatorValue);

        Assert.NotNull(page.OdataNextLink);
        var partners = page.Value!.Select(GraphDirectoryReader.MapCrossTenantAccessPartner).ToList();

        var first = partners[0];
        Assert.Equal("123f4846-ba00-4fd7-ba43-dac1f8f63013", first.TenantId);
        Assert.Null(first.IsServiceProvider);
        Assert.Null(first.InboundTrust.IsMfaAccepted);
        Assert.Null(first.AutomaticRedemptionInbound);
        Assert.Null(first.B2bCollaborationInbound);
        Assert.Null(first.B2bDirectConnectOutbound);
        Assert.True(first.B2bDirectConnectInbound!.IsAllowedForAny);
        Assert.False(first.B2bDirectConnectInbound.IsOpenToAll);

        var second = partners[1];
        Assert.True(second.IsServiceProvider);
        Assert.True(second.InboundTrust.IsMfaAccepted);
        Assert.Null(second.InboundTrust.IsCompliantDeviceAccepted);
        Assert.False(second.InboundTrust.IsHybridJoinedDeviceAccepted);
        Assert.True(second.AutomaticRedemptionInbound);
        Assert.False(second.AutomaticRedemptionOutbound);
    }

    [Fact]
    public void Partner_with_every_property_null_inherits_everything()
    {
        var partner = GraphDirectoryReader.MapCrossTenantAccessPartner(new CrossTenantAccessPolicyConfigurationPartner());

        Assert.Equal(string.Empty, partner.TenantId);
        Assert.Null(partner.IsServiceProvider);
        Assert.Null(partner.InboundTrust.IsMfaAccepted);
        Assert.Null(partner.InboundTrust.IsCompliantDeviceAccepted);
        Assert.Null(partner.InboundTrust.IsHybridJoinedDeviceAccepted);
        Assert.Null(partner.AutomaticRedemptionInbound);
        Assert.Null(partner.AutomaticRedemptionOutbound);
        Assert.Null(partner.B2bCollaborationInbound);
        Assert.Null(partner.B2bCollaborationOutbound);
        Assert.Null(partner.B2bDirectConnectInbound);
        Assert.Null(partner.B2bDirectConnectOutbound);
    }

    private static AuthorizationPolicySettings MapAuthorizationPolicy(string json)
    {
        using var document = JsonDocument.Parse(json);
        return GraphDirectoryReader.MapAuthorizationPolicy(document.RootElement);
    }

    private static async Task<T> ParseAsync<T>(string json, ParsableFactory<T> factory)
        where T : IParsable
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var node = await new JsonParseNodeFactory().GetRootParseNodeAsync("application/json", stream);
        return node.GetObjectValue(factory)!;
    }
}
