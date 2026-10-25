using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Entraspect.Core;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Serialization.Json;
using RiskLevel = Entraspect.Core.RiskLevel;
using RiskState = Entraspect.Core.RiskState;
using RiskyUser = Entraspect.Core.RiskyUser;

namespace Entraspect.Graph.Tests;

// Mapping tests only: they build Graph SDK objects and sample JSON in memory and never create a client or credential.
public sealed class UserHygieneMappingTests
{
    private const string UserId = "aaaaaaaa-0000-0000-0000-000000000001";

    [Fact]
    public void Maps_a_synced_user_with_licences()
    {
        var result = GraphDirectoryReader.MapUser(new User
        {
            Id = UserId,
            OnPremisesSyncEnabled = true,
            AssignedLicenses =
            [
                new AssignedLicense { SkuId = Guid.NewGuid() },
                new AssignedLicense { SkuId = Guid.NewGuid(), DisabledPlans = [Guid.NewGuid()] },
            ],
        });

        Assert.True(result.OnPremisesSyncEnabled);
        Assert.Equal(2, result.AssignedLicenceCount);
    }

    [Fact]
    public void Missing_sync_and_licence_values_mean_cloud_only_and_none()
    {
        var result = GraphDirectoryReader.MapUser(new User { Id = UserId });

        Assert.Null(result.OnPremisesSyncEnabled);
        Assert.Equal(0, result.AssignedLicenceCount);
    }

    [Fact]
    public void Empty_licences_and_entries_without_a_sku_count_as_none()
    {
        Assert.Equal(0, GraphDirectoryReader.MapAssignedLicenceCount([]));
        Assert.Equal(0, GraphDirectoryReader.MapAssignedLicenceCount([new AssignedLicense()]));
        Assert.Equal(0, GraphDirectoryReader.MapAssignedLicenceCount(null));
    }

    [Fact]
    public async Task Users_page_with_sync_and_licences_parses()
    {
        const string json =
            """
            {
              "value": [
                {
                  "id": "aaaaaaaa-0000-0000-0000-000000000001",
                  "onPremisesSyncEnabled": null,
                  "assignedLicenses": []
                },
                {
                  "id": "aaaaaaaa-0000-0000-0000-000000000002",
                  "onPremisesSyncEnabled": true,
                  "assignedLicenses": [ { "disabledPlans": [], "skuId": "c7df2760-2c81-4ef7-b578-5b5392b571df" } ]
                }
              ]
            }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var node = await new JsonParseNodeFactory().GetRootParseNodeAsync("application/json", stream);

        var users = node.GetObjectValue(UserCollectionResponse.CreateFromDiscriminatorValue)!.Value!
            .Select(GraphDirectoryReader.MapUser)
            .ToList();

        Assert.Equal((null, 0), (users[0].OnPremisesSyncEnabled, users[0].AssignedLicenceCount));
        Assert.Equal((true, 1), (users[1].OnPremisesSyncEnabled, users[1].AssignedLicenceCount));
    }

    [Fact]
    public void Maps_a_page_of_risky_users()
    {
        var result = new List<RiskyUser>();

        var next = AddPage(
            """
            {
              "@odata.nextLink": "https://graph.microsoft.com/v1.0/identityProtection/riskyUsers?$skiptoken=abc",
              "value": [
                {
                  "id": "aaaaaaaa-0000-0000-0000-000000000001",
                  "isDeleted": false,
                  "isProcessing": false,
                  "riskLastUpdatedDateTime": "2026-10-20T08:00:00Z",
                  "riskLevel": "high",
                  "riskState": "atRisk",
                  "riskDetail": "none",
                  "userDisplayName": "User 1",
                  "userPrincipalName": "user1@contoso.com"
                }
              ]
            }
            """,
            result,
            filterStates: false);

        Assert.Equal("https://graph.microsoft.com/v1.0/identityProtection/riskyUsers?$skiptoken=abc", next);
        var user = Assert.Single(result);
        Assert.Equal(UserId, user.Id);
        Assert.Equal(("User 1", "user1@contoso.com"), (user.DisplayName, user.UserPrincipalName));
        Assert.Equal((RiskLevel.High, RiskState.AtRisk, "none"), (user.RiskLevel, user.RiskState, user.RiskDetail));
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero), user.RiskLastUpdated);
        Assert.False(user.IsDeleted);
    }

    [Fact]
    public void Missing_and_unknown_risk_values_never_throw()
    {
        var result = new List<RiskyUser>();

        var next = AddPage(
            """
            {
              "value": [
                { "id": "aaaaaaaa-0000-0000-0000-000000000001", "riskLevel": "unknownFutureValue", "riskState": "somethingNew" },
                {
                  "id": "aaaaaaaa-0000-0000-0000-000000000002",
                  "riskLevel": 3,
                  "riskLastUpdatedDateTime": "not a date",
                  "isDeleted": "yes",
                  "userDisplayName": null
                },
                { "riskState": "atRisk" },
                "not a user"
              ]
            }
            """,
            result,
            filterStates: false);

        Assert.Null(next);
        Assert.Equal(2, result.Count);
        Assert.All(result, u => Assert.Equal((RiskLevel.Unknown, RiskState.Unknown), (u.RiskLevel, u.RiskState)));
        Assert.All(result, u => Assert.Null(u.RiskLastUpdated));
        Assert.All(result, u => Assert.False(u.IsDeleted));
        Assert.All(result, u => Assert.Equal(string.Empty, u.RiskDetail));
        Assert.Equal(string.Empty, result[1].DisplayName);
    }

    [Fact]
    public void Fallback_page_keeps_only_users_at_risk_or_confirmed_compromised()
    {
        var result = new List<RiskyUser>();

        AddPage(
            """
            {
              "value": [
                { "id": "u1", "riskState": "atRisk" },
                { "id": "u2", "riskState": "confirmedCompromised" },
                { "id": "u3", "riskState": "remediated" },
                { "id": "u4", "riskState": "dismissed" },
                { "id": "u5", "riskState": "confirmedSafe" },
                { "id": "u6", "riskState": "none" },
                { "id": "u7" }
              ]
            }
            """,
            result,
            filterStates: true);

        Assert.Equal(["u1", "u2"], result.Select(u => u.Id));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"value\": null}")]
    public void Unexpected_risky_user_pages_do_not_throw(string json)
    {
        var result = new List<RiskyUser>();

        Assert.Null(AddPage(json, result, filterStates: false));
        Assert.Empty(result);
    }

    [Theory]
    [InlineData("low", RiskLevel.Low)]
    [InlineData("Medium", RiskLevel.Medium)]
    [InlineData(" HIGH ", RiskLevel.High)]
    [InlineData("hidden", RiskLevel.Hidden)]
    [InlineData("none", RiskLevel.None)]
    [InlineData("unknownFutureValue", RiskLevel.Unknown)]
    [InlineData("", RiskLevel.Unknown)]
    [InlineData(null, RiskLevel.Unknown)]
    public void Maps_risk_levels(string? value, RiskLevel expected)
    {
        Assert.Equal(expected, GraphDirectoryReader.MapRiskLevel(value));
    }

    [Theory]
    [InlineData("atRisk", RiskState.AtRisk)]
    [InlineData("confirmedCompromised", RiskState.ConfirmedCompromised)]
    [InlineData("REMEDIATED", RiskState.Remediated)]
    [InlineData("dismissed", RiskState.Dismissed)]
    [InlineData("confirmedSafe", RiskState.ConfirmedSafe)]
    [InlineData("none", RiskState.None)]
    [InlineData("unknownFutureValue", RiskState.Unknown)]
    [InlineData(null, RiskState.Unknown)]
    public void Maps_risk_states(string? value, RiskState expected)
    {
        Assert.Equal(expected, GraphDirectoryReader.MapRiskState(value));
    }

    [Fact]
    public void Risky_user_urls_filter_the_two_states_or_read_every_user()
    {
        Assert.Equal(
            "https://graph.microsoft.com/v1.0/identityProtection/riskyUsers?$filter=" +
            "riskState%20eq%20%27atRisk%27%20or%20riskState%20eq%20%27confirmedCompromised%27&$top=500",
            GraphDirectoryReader.RiskyUsersUrl());
        Assert.Equal(
            "https://graph.microsoft.com/v1.0/identityProtection/riskyUsers?$top=500",
            GraphDirectoryReader.AllRiskyUsersUrl());
    }

    private static string? AddPage(string json, List<RiskyUser> result, bool filterStates)
    {
        using var document = JsonDocument.Parse(json);
        return GraphDirectoryReader.AddRiskyUsers(document.RootElement, result, filterStates);
    }
}
