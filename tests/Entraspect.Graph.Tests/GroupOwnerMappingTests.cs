using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Entraspect.Core;

namespace Entraspect.Graph.Tests;

// Mapping tests only: they parse sample JSON in memory and never create a client or credential.
public sealed class GroupOwnerMappingTests
{
    private const string UserId = "aaaaaaaa-0000-0000-0000-000000000001";
    private const string ServicePrincipalId = "bbbbbbbb-0000-0000-0000-000000000001";
    private const string DeviceId = "cccccccc-0000-0000-0000-000000000001";

    [Fact]
    public void Group_owners_url_reads_v1_owners_without_query_parameters()
    {
        Assert.Equal(
            $"https://graph.microsoft.com/v1.0/groups/{UserId}/owners",
            GraphDirectoryReader.GroupOwnersUrl(UserId));
    }

    [Fact]
    public void Maps_users_service_principals_and_other_kinds_and_returns_the_next_link()
    {
        var result = new List<GroupOwner>();

        var next = AddPage(
            $$"""
            {
              "@odata.nextLink": "https://graph.microsoft.com/v1.0/groups/g1/owners?$skiptoken=abc",
              "value": [
                { "@odata.type": "#microsoft.graph.user", "id": "{{UserId}}", "displayName": "Ada", "userPrincipalName": "ada@contoso.com" },
                { "@odata.type": "#microsoft.graph.servicePrincipal", "id": "{{ServicePrincipalId}}", "displayName": "Deploy app", "userPrincipalName": "ignored" },
                { "@odata.type": "#microsoft.graph.device", "id": "{{DeviceId}}", "displayName": "Laptop" }
              ]
            }
            """,
            result);

        Assert.Equal("https://graph.microsoft.com/v1.0/groups/g1/owners?$skiptoken=abc", next);
        Assert.Equal(
            [
                (UserId, GroupOwnerKind.User, "Ada", "ada@contoso.com"),
                (ServicePrincipalId, GroupOwnerKind.ServicePrincipal, "Deploy app", null),
                (DeviceId, GroupOwnerKind.Other, "Laptop", null),
            ],
            result.Select(o => (o.Id, o.Kind, o.DisplayName, o.UserPrincipalName)));
    }

    [Fact]
    public void Type_is_matched_ignoring_case()
    {
        var owner = Map($$"""{ "@odata.type": "#Microsoft.Graph.ServicePrincipal", "id": "{{ServicePrincipalId}}" }""");

        Assert.Equal(GroupOwnerKind.ServicePrincipal, owner!.Kind);
    }

    [Fact]
    public void Missing_or_unexpected_values_never_throw()
    {
        var owner = Map($$"""{ "id": "{{UserId}}", "displayName": 42, "userPrincipalName": null }""");

        Assert.Equal((UserId, GroupOwnerKind.Other, null, null),
            (owner!.Id, owner.Kind, owner.DisplayName, owner.UserPrincipalName));
    }

    [Fact]
    public void Empty_name_and_upn_are_null()
    {
        var owner = Map($$"""{ "@odata.type": "#microsoft.graph.user", "id": "{{UserId}}", "displayName": "", "userPrincipalName": "" }""");

        Assert.Equal((GroupOwnerKind.User, null, null), (owner!.Kind, owner.DisplayName, owner.UserPrincipalName));
    }

    [Theory]
    [InlineData("""{ "@odata.type": "#microsoft.graph.user" }""")]
    [InlineData("""{ "id": "" }""")]
    [InlineData("""{ "id": 7 }""")]
    [InlineData("""[]""")]
    public void Owner_without_an_id_is_skipped(string json)
    {
        Assert.Null(Map(json));
    }

    [Theory]
    [InlineData("""{ "value": [] }""")]
    [InlineData("""{ }""")]
    [InlineData("""{ "value": {} }""")]
    [InlineData("""[]""")]
    public void Last_or_unexpected_page_has_no_next_link_and_adds_nothing(string json)
    {
        var result = new List<GroupOwner>();

        Assert.Null(AddPage(json, result));
        Assert.Empty(result);
    }

    private static GroupOwner? Map(string json)
    {
        using var document = JsonDocument.Parse(json);
        return GraphDirectoryReader.MapGroupOwner(document.RootElement);
    }

    private static string? AddPage(string json, List<GroupOwner> result)
    {
        using var document = JsonDocument.Parse(json);
        return GraphDirectoryReader.AddGroupOwners(document.RootElement, result);
    }
}
