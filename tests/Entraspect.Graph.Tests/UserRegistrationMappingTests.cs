using System.IO;
using System.Text;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Serialization.Json;
using CoreUserType = Entraspect.Core.UserType;

namespace Entraspect.Graph.Tests;

// Mapping tests only: they build Graph SDK objects in memory and never create a client or credential.
public sealed class UserRegistrationMappingTests
{
    private static readonly DateTimeOffset Updated = new(2026, 10, 24, 6, 15, 41, TimeSpan.Zero);

    [Fact]
    public void Maps_a_member_who_is_not_registered()
    {
        var result = GraphDirectoryReader.MapUserRegistration(new UserRegistrationDetails
        {
            Id = "86462606-fde0-4fc4-9e0c-a20eb73e54c6",
            UserPrincipalName = "alexw@contoso.example",
            UserDisplayName = "Alex Wilber",
            UserType = SignInUserType.Member,
            IsAdmin = false,
            IsMfaRegistered = false,
            IsMfaCapable = false,
            MethodsRegistered = [],
            LastUpdatedDateTime = Updated,
        });

        Assert.Equal("86462606-fde0-4fc4-9e0c-a20eb73e54c6", result.Id);
        Assert.Equal("alexw@contoso.example", result.UserPrincipalName);
        Assert.Equal("Alex Wilber", result.DisplayName);
        Assert.Equal(CoreUserType.Member, result.UserType);
        Assert.False(result.IsAdmin);
        Assert.False(result.IsMfaRegistered);
        Assert.False(result.IsMfaCapable);
        Assert.Empty(result.MethodsRegistered);
        Assert.Equal(Updated, result.LastUpdatedDateTime);
    }

    [Fact]
    public void Maps_a_guest_admin_with_registered_methods()
    {
        var result = GraphDirectoryReader.MapUserRegistration(new UserRegistrationDetails
        {
            Id = "u2",
            UserType = SignInUserType.Guest,
            IsAdmin = true,
            IsMfaRegistered = true,
            IsMfaCapable = false,
            MethodsRegistered = ["email", "", "mobilePhone"],
        });

        Assert.Equal(CoreUserType.Guest, result.UserType);
        Assert.True(result.IsAdmin);
        Assert.True(result.IsMfaRegistered);
        Assert.Equal(new[] { "email", "mobilePhone" }, result.MethodsRegistered);
    }

    [Fact]
    public void Keeps_the_registered_methods_of_a_capable_admin_as_Graph_names_them()
    {
        var result = GraphDirectoryReader.MapUserRegistration(new UserRegistrationDetails
        {
            Id = "u4",
            IsAdmin = true,
            IsMfaRegistered = true,
            IsMfaCapable = true,
            MethodsRegistered = ["passKeyDeviceBoundAuthenticator", "WindowsHelloForBusiness", "microsoftAuthenticatorPush"],
        });

        Assert.True(result.IsMfaCapable);
        Assert.Equal(
            new[] { "passKeyDeviceBoundAuthenticator", "WindowsHelloForBusiness", "microsoftAuthenticatorPush" },
            result.MethodsRegistered);
    }

    [Fact]
    public void Missing_methods_are_empty()
    {
        var result = GraphDirectoryReader.MapUserRegistration(new UserRegistrationDetails { MethodsRegistered = null });

        Assert.Empty(result.MethodsRegistered);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(SignInUserType.UnknownFutureValue)]
    public void Missing_or_unknown_user_type_is_a_member(SignInUserType? userType)
    {
        var result = GraphDirectoryReader.MapUserRegistration(new UserRegistrationDetails { UserType = userType });

        Assert.Equal(CoreUserType.Member, result.UserType);
    }

    [Fact]
    public void Missing_values_do_not_throw_and_mean_not_capable()
    {
        var result = GraphDirectoryReader.MapUserRegistration(new UserRegistrationDetails());

        Assert.Equal(string.Empty, result.Id);
        Assert.Equal(string.Empty, result.UserPrincipalName);
        Assert.Equal(string.Empty, result.DisplayName);
        Assert.False(result.IsAdmin);
        Assert.False(result.IsMfaRegistered);
        Assert.False(result.IsMfaCapable);
        Assert.Empty(result.MethodsRegistered);
        Assert.Null(result.LastUpdatedDateTime);
    }

    [Fact]
    public async Task Maps_a_report_page_as_Graph_returns_it()
    {
        // Shape of the List userRegistrationDetails response in the Graph docs, with a value Graph may add later.
        const string json = """
            {
              "@odata.nextLink": "https://graph.microsoft.com/v1.0/reports/authenticationMethods/userRegistrationDetails?$skiptoken=x",
              "value": [
                {
                  "id": "c6ad1942-4afa-47f8-8d48-afb5d8d69d2f",
                  "userPrincipalName": "allan@contoso.example",
                  "userDisplayName": "Allan Deyoung",
                  "isAdmin": false,
                  "isMfaRegistered": false,
                  "isMfaCapable": false,
                  "lastUpdatedDateTime": "2026-10-24T06:15:41.6195833Z",
                  "methodsRegistered": [],
                  "userType": "guest"
                },
                {
                  "id": "u3",
                  "userDisplayName": "Synced Admin",
                  "isAdmin": true,
                  "isMfaRegistered": true,
                  "isMfaCapable": false,
                  "methodsRegistered": ["someNewMethod"],
                  "userType": "someNewType"
                }
              ]
            }
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var node = await new JsonParseNodeFactory().GetRootParseNodeAsync("application/json", stream);
        var page = node.GetObjectValue(UserRegistrationDetailsCollectionResponse.CreateFromDiscriminatorValue)!;

        var users = page.Value!.Select(GraphDirectoryReader.MapUserRegistration).ToList();

        Assert.NotNull(page.OdataNextLink);
        Assert.Equal(2, users.Count);
        Assert.Equal(CoreUserType.Guest, users[0].UserType);
        Assert.Equal("Allan Deyoung", users[0].DisplayName);
        Assert.Equal(new DateTimeOffset(2026, 10, 24, 6, 15, 41, TimeSpan.Zero), users[0].LastUpdatedDateTime!.Value.AddTicks(-6195833));
        Assert.Equal(CoreUserType.Member, users[1].UserType);
        Assert.True(users[1].IsAdmin);
        Assert.True(users[1].IsMfaRegistered);
        Assert.Equal(string.Empty, users[1].UserPrincipalName);
        Assert.Equal(new[] { "someNewMethod" }, users[1].MethodsRegistered);
    }
}
