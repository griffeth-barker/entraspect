using System.Text.Json;
using Entraspect.Core;
using Microsoft.Graph.Models;
using UserType = Entraspect.Core.UserType;

namespace Entraspect.Graph.Tests;

// Mapping tests only: they build Graph SDK objects and sample JSON in memory and never create a client or credential.
public sealed class MfaRequirementMappingTests
{
    private const string UserId = "aaaaaaaa-0000-0000-0000-000000000001";
    private const string OtherUserId = "aaaaaaaa-0000-0000-0000-000000000002";

    [Fact]
    public void Maps_a_user()
    {
        var result = GraphDirectoryReader.MapUser(new User
        {
            Id = UserId,
            DisplayName = "Adele Vance",
            UserPrincipalName = "adele@contoso.com",
            UserType = "Guest",
            AccountEnabled = false,
        });

        Assert.Equal((UserId, "Adele Vance", "adele@contoso.com"), (result.Id, result.DisplayName, result.UserPrincipalName));
        Assert.Equal(UserType.Guest, result.UserType);
        Assert.False(result.AccountEnabled);
    }

    [Fact]
    public void Missing_user_values_map_to_an_enabled_member()
    {
        var result = GraphDirectoryReader.MapUser(new User { Id = UserId });

        Assert.Equal(string.Empty, result.DisplayName);
        Assert.Equal(string.Empty, result.UserPrincipalName);
        Assert.Equal(UserType.Member, result.UserType);
        Assert.True(result.AccountEnabled);
    }

    [Fact]
    public void Maps_a_role_assignment_and_skips_incomplete_ones()
    {
        var result = GraphDirectoryReader.MapRoleAssignment(new UnifiedRoleAssignment
        {
            RoleDefinitionId = "62e90394-69f5-4237-9190-012177145e10",
            PrincipalId = UserId,
        });

        Assert.Equal(("62e90394-69f5-4237-9190-012177145e10", UserId), (result!.RoleDefinitionId, result.PrincipalId));
        Assert.Null(GraphDirectoryReader.MapRoleAssignment(new UnifiedRoleAssignment { PrincipalId = UserId }));
        Assert.Null(GraphDirectoryReader.MapRoleAssignment(new UnifiedRoleAssignment { RoleDefinitionId = "r" }));
    }

    [Theory]
    [InlineData("""{"perUserMfaState":"disabled"}""", PerUserMfaState.Disabled)]
    [InlineData("""{"perUserMfaState":"enabled"}""", PerUserMfaState.Enabled)]
    [InlineData("""{"perUserMfaState":"enforced"}""", PerUserMfaState.Enforced)]
    [InlineData("""{"perUserMfaState":"Enforced"}""", PerUserMfaState.Enforced)]
    [InlineData("""{"perUserMfaState":"unknownFutureValue"}""", PerUserMfaState.Unknown)]
    [InlineData("""{"perUserMfaState":null}""", PerUserMfaState.Unknown)]
    [InlineData("""{"perUserMfaState":2}""", PerUserMfaState.Unknown)]
    [InlineData("""{}""", PerUserMfaState.Unknown)]
    [InlineData("""[]""", PerUserMfaState.Unknown)]
    public void Maps_the_beta_per_user_mfa_state(string json, PerUserMfaState expected)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Equal(expected, GraphDirectoryReader.MapPerUserMfaState(document.RootElement));
    }

    [Fact]
    public void Adds_successful_single_factor_sign_ins_per_user_and_returns_the_next_link()
    {
        const string json = $$"""
            {
              "@odata.nextLink": "https://graph.microsoft.com/beta/auditLogs/signIns?$skiptoken=abc",
              "value": [
                { "userId": "{{UserId}}", "createdDateTime": "2026-10-20T08:00:00Z",
                  "authenticationRequirement": "singleFactorAuthentication", "status": { "errorCode": 0 } },
                { "userId": "{{UserId}}", "createdDateTime": "2026-10-22T09:30:00Z",
                  "authenticationRequirement": "singleFactorAuthentication", "status": { "errorCode": 0 } },
                { "userId": "{{OtherUserId}}", "createdDateTime": "2026-10-21T10:00:00Z",
                  "authenticationRequirement": "singleFactorAuthentication", "status": { "errorCode": 0, "failureReason": "Other." } }
              ]
            }
            """;
        using var document = JsonDocument.Parse(json);
        var totals = new Dictionary<string, SingleFactorSignIns>(StringComparer.OrdinalIgnoreCase);

        var next = GraphDirectoryReader.AddSingleFactorSignIns(document.RootElement, totals);

        Assert.Equal("https://graph.microsoft.com/beta/auditLogs/signIns?$skiptoken=abc", next);
        Assert.Equal(new SingleFactorSignIns(2, new DateTimeOffset(2026, 10, 22, 9, 30, 0, TimeSpan.Zero)), totals[UserId]);
        Assert.Equal(1, totals[OtherUserId].Count);
    }

    [Fact]
    public void Later_pages_add_to_the_totals_and_the_last_page_has_no_next_link()
    {
        var totals = new Dictionary<string, SingleFactorSignIns>(StringComparer.OrdinalIgnoreCase)
        {
            [UserId] = new(3, new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.Zero)),
        };
        const string json = $$"""
            {
              "value": [
                { "userId": "{{UserId}}", "createdDateTime": "2026-10-19T00:00:00Z",
                  "authenticationRequirement": "singleFactorAuthentication", "status": { "errorCode": 0 } }
              ]
            }
            """;
        using var document = JsonDocument.Parse(json);

        Assert.Null(GraphDirectoryReader.AddSingleFactorSignIns(document.RootElement, totals));
        Assert.Equal(new SingleFactorSignIns(4, new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.Zero)), totals[UserId]);
    }

    [Fact]
    public void Skips_failed_multifactor_and_incomplete_sign_ins_without_throwing()
    {
        const string json = $$"""
            {
              "value": [
                { "userId": "{{UserId}}", "createdDateTime": "2026-10-20T08:00:00Z",
                  "authenticationRequirement": "singleFactorAuthentication", "status": { "errorCode": 50126 } },
                { "userId": "{{UserId}}", "createdDateTime": "2026-10-20T08:00:00Z",
                  "authenticationRequirement": "multiFactorAuthentication", "status": { "errorCode": 0 } },
                { "userId": "{{UserId}}", "createdDateTime": "2026-10-20T08:00:00Z",
                  "authenticationRequirement": "singleFactorAuthentication" },
                { "userId": "{{UserId}}", "createdDateTime": "2026-10-20T08:00:00Z",
                  "authenticationRequirement": "singleFactorAuthentication", "status": { "errorCode": "0" } },
                { "userId": "", "createdDateTime": "2026-10-20T08:00:00Z",
                  "authenticationRequirement": "singleFactorAuthentication", "status": { "errorCode": 0 } },
                { "createdDateTime": "2026-10-20T08:00:00Z",
                  "authenticationRequirement": "singleFactorAuthentication", "status": { "errorCode": 0 } },
                { "userId": "{{UserId}}", "createdDateTime": "not a date",
                  "authenticationRequirement": "singleFactorAuthentication", "status": { "errorCode": 0 } },
                { "userId": "{{UserId}}", "authenticationRequirement": "singleFactorAuthentication", "status": null },
                "not an object",
                null
              ],
              "@odata.nextLink": null
            }
            """;
        using var document = JsonDocument.Parse(json);
        var totals = new Dictionary<string, SingleFactorSignIns>();

        Assert.Null(GraphDirectoryReader.AddSingleFactorSignIns(document.RootElement, totals));
        Assert.Empty(totals);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"value":null}""")]
    [InlineData("""{"value":{}}""")]
    [InlineData("""[]""")]
    public void A_page_without_a_value_list_adds_nothing(string json)
    {
        using var document = JsonDocument.Parse(json);
        var totals = new Dictionary<string, SingleFactorSignIns>();

        Assert.Null(GraphDirectoryReader.AddSingleFactorSignIns(document.RootElement, totals));
        Assert.Empty(totals);
    }

    [Fact]
    public void Sign_in_url_filters_on_the_server_for_successful_single_factor_sign_ins()
    {
        var url = GraphDirectoryReader.SingleFactorSignInsUrl(new DateTimeOffset(2026, 10, 18, 4, 0, 0, TimeSpan.FromHours(-7)));
        var query = Uri.UnescapeDataString(new Uri(url).Query);

        Assert.StartsWith("https://graph.microsoft.com/beta/auditLogs/signIns?", url);
        Assert.Contains(
            "$filter=createdDateTime ge 2026-10-18T11:00:00Z and authenticationRequirement eq " +
            "'singleFactorAuthentication' and status/errorCode eq 0",
            query);
        Assert.Contains("$top=1000", query);
        Assert.Contains("$select=userId,createdDateTime,authenticationRequirement,status", query);
        Assert.DoesNotContain("signInEventTypes", query);
    }
}
