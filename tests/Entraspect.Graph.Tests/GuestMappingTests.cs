using System.IO;
using System.Text;
using System.Text.Json;
using Entraspect.Core;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Serialization.Json;

namespace Entraspect.Graph.Tests;

// Mapping tests only: they build Graph SDK objects and sample JSON in memory and never create a client or credential.
public sealed class GuestMappingTests
{
    private const string GuestId = "aaaaaaaa-0000-0000-0000-000000000001";
    private const string MemberId = "aaaaaaaa-0000-0000-0000-000000000002";

    [Fact]
    public void Maps_a_guest_with_its_invitation()
    {
        var created = new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var changed = new DateTimeOffset(2025, 1, 3, 0, 0, 0, TimeSpan.Zero);

        var result = GraphDirectoryReader.MapUser(new User
        {
            Id = GuestId,
            UserType = "Guest",
            CreatedDateTime = created,
            ExternalUserState = "PendingAcceptance",
            ExternalUserStateChangeDateTime = changed,
        });

        Assert.Equal(created, result.CreatedDateTime);
        Assert.Equal(ExternalUserState.PendingAcceptance, result.ExternalUserState);
        Assert.Equal(changed, result.ExternalUserStateChangeDateTime);
    }

    [Fact]
    public void Missing_invitation_values_are_null()
    {
        var result = GraphDirectoryReader.MapUser(new User { Id = GuestId });

        Assert.Null(result.CreatedDateTime);
        Assert.Null(result.ExternalUserState);
        Assert.Null(result.ExternalUserStateChangeDateTime);
    }

    [Theory]
    [InlineData("PendingAcceptance", ExternalUserState.PendingAcceptance)]
    [InlineData("pendingacceptance", ExternalUserState.PendingAcceptance)]
    [InlineData("Accepted", ExternalUserState.Accepted)]
    [InlineData(" accepted ", ExternalUserState.Accepted)]
    [InlineData("SomethingNew", ExternalUserState.Unknown)]
    [InlineData(null, null)]
    [InlineData("", null)]
    public void Maps_the_external_user_state(string? value, ExternalUserState? expected)
    {
        Assert.Equal(expected, GraphDirectoryReader.MapExternalUserState(value));
    }

    [Fact]
    public async Task Users_page_with_empty_invitation_values_parses()
    {
        // The SDK parses the users page; an empty or null date must not fail the read that other checks share.
        const string json = """
            {
              "value": [
                {
                  "id": "aaaaaaaa-0000-0000-0000-000000000001",
                  "userType": "Guest",
                  "createdDateTime": null,
                  "externalUserState": null,
                  "externalUserStateChangeDateTime": ""
                }
              ]
            }
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var node = await new JsonParseNodeFactory().GetRootParseNodeAsync("application/json", stream);
        var page = node.GetObjectValue(UserCollectionResponse.CreateFromDiscriminatorValue)!;
        var user = GraphDirectoryReader.MapUser(Assert.Single(page.Value!));

        Assert.Null(user.CreatedDateTime);
        Assert.Null(user.ExternalUserState);
        Assert.Null(user.ExternalUserStateChangeDateTime);
    }

    [Fact]
    public void Maps_a_page_of_sign_in_activity()
    {
        var result = new List<GuestSignInActivity>();

        var next = AddPage(
            """
            {
              "@odata.nextLink": "https://graph.microsoft.com/v1.0/users?$skiptoken=abc",
              "value": [
                {
                  "id": "aaaaaaaa-0000-0000-0000-000000000001",
                  "signInActivity": {
                    "lastSuccessfulSignInDateTime": "2026-03-01T10:00:00Z",
                    "lastSignInDateTime": "2026-02-01T10:00:00Z",
                    "lastNonInteractiveSignInDateTime": "2026-04-01T10:00:00Z"
                  }
                }
              ]
            }
            """,
            result,
            guestsOnly: false);

        Assert.Equal("https://graph.microsoft.com/v1.0/users?$skiptoken=abc", next);
        var activity = Assert.Single(result);
        Assert.Equal(GuestId, activity.UserId);
        Assert.Equal(new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero), activity.LastSuccessfulSignIn);
        Assert.Equal(new DateTimeOffset(2026, 2, 1, 10, 0, 0, TimeSpan.Zero), activity.LastSignIn);
        Assert.Equal(new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero), activity.LastNonInteractiveSignIn);
    }

    [Fact]
    public void Never_values_and_missing_activity_map_to_null()
    {
        var result = new List<GuestSignInActivity>();

        var next = AddPage(
            """
            {
              "value": [
                {
                  "id": "aaaaaaaa-0000-0000-0000-000000000001",
                  "signInActivity": {
                    "lastSuccessfulSignInDateTime": "0001-01-01T00:00:00Z",
                    "lastSignInDateTime": "",
                    "lastNonInteractiveSignInDateTime": "not a date"
                  }
                },
                { "id": "aaaaaaaa-0000-0000-0000-000000000002", "signInActivity": null },
                { "id": "aaaaaaaa-0000-0000-0000-000000000003" },
                { "signInActivity": {} },
                "not a user"
              ]
            }
            """,
            result,
            guestsOnly: false);

        Assert.Null(next);
        Assert.Equal(3, result.Count);
        Assert.All(result, a => Assert.Null(a.LastSuccessfulSignIn));
        Assert.All(result, a => Assert.Null(a.LastSignIn));
        Assert.All(result, a => Assert.Null(a.LastNonInteractiveSignIn));
    }

    [Fact]
    public void Fallback_page_keeps_only_guests()
    {
        var result = new List<GuestSignInActivity>();

        AddPage(
            $$"""
            {
              "value": [
                { "id": "{{GuestId}}", "userType": "Guest", "signInActivity": {} },
                { "id": "{{MemberId}}", "userType": "Member", "signInActivity": {} },
                { "id": "aaaaaaaa-0000-0000-0000-000000000003", "signInActivity": {} }
              ]
            }
            """,
            result,
            guestsOnly: true);

        Assert.Equal(GuestId, Assert.Single(result).UserId);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"value\": null}")]
    public void Unexpected_pages_do_not_throw(string json)
    {
        var result = new List<GuestSignInActivity>();

        Assert.Null(AddPage(json, result, guestsOnly: false));
        Assert.Empty(result);
    }

    [Theory]
    [InlineData("0001-01-01T00:00:00Z", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("garbage", false)]
    [InlineData("2024-05-06T07:08:09Z", true)]
    public void Maps_sign_in_dates(string? value, bool isDate)
    {
        Assert.Equal(isDate, GraphDirectoryReader.MapSignInDate(value).HasValue);
    }

    [Fact]
    public void Sign_in_activity_urls_filter_guests_or_select_the_user_type()
    {
        Assert.Equal(
            "https://graph.microsoft.com/v1.0/users?$filter=userType%20eq%20%27Guest%27&$select=id,signInActivity&$top=500",
            GraphDirectoryReader.GuestSignInActivityUrl());
        Assert.Equal(
            "https://graph.microsoft.com/v1.0/users?$select=id,userType,signInActivity&$top=500",
            GraphDirectoryReader.AllUsersSignInActivityUrl());
    }

    [Fact]
    public void Maps_role_definitions_defensively()
    {
        var builtIn = GraphDirectoryReader.MapRoleDefinition(new UnifiedRoleDefinition
        {
            Id = "f2ef992c-3afb-46b9-b7cf-a126ee74c451",
            TemplateId = "f2ef992c-3afb-46b9-b7cf-a126ee74c451",
            DisplayName = "Global Reader",
        });
        var custom = GraphDirectoryReader.MapRoleDefinition(new UnifiedRoleDefinition
        {
            Id = "cccccccc-0000-0000-0000-000000000001",
            TemplateId = "",
            DisplayName = " ",
        });

        Assert.Equal(("Global Reader", "f2ef992c-3afb-46b9-b7cf-a126ee74c451"), (builtIn!.DisplayName, builtIn.TemplateId));
        Assert.Equal("cccccccc-0000-0000-0000-000000000001", custom!.Id);
        Assert.Null(custom.TemplateId);
        Assert.Null(custom.DisplayName);
        Assert.Null(GraphDirectoryReader.MapRoleDefinition(new UnifiedRoleDefinition { DisplayName = "No ID" }));
    }

    private static string? AddPage(string json, List<GuestSignInActivity> result, bool guestsOnly)
    {
        using var document = JsonDocument.Parse(json);
        return GraphDirectoryReader.AddGuestSignInActivity(document.RootElement, result, guestsOnly);
    }
}
