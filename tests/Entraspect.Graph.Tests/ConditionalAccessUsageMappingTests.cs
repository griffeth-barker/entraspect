using System.IO;
using System.Linq;
using System.Text;
using Entraspect.Core;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Serialization.Json;

namespace Entraspect.Graph.Tests;

// Mapping tests only: they build Graph SDK objects and sample JSON in memory and never create a client or credential.
public sealed class ConditionalAccessUsageMappingTests
{
    private const string PolicyA = "aaaaaaaa-1111-0000-0000-000000000001";
    private const string PolicyB = "aaaaaaaa-1111-0000-0000-000000000002";

    [Fact]
    public void Maps_each_applied_policy_id_and_result()
    {
        var applied = GraphDirectoryReader.MapAppliedPolicies(new SignIn
        {
            AppliedConditionalAccessPolicies =
            [
                new AppliedConditionalAccessPolicy { Id = PolicyA, Result = AppliedConditionalAccessPolicyResult.Success },
                new AppliedConditionalAccessPolicy
                {
                    Id = PolicyB, Result = AppliedConditionalAccessPolicyResult.ReportOnlyNotApplied,
                },
            ],
        });

        Assert.Equal(
            new[]
            {
                new AppliedPolicyResult(PolicyA, ConditionalAccessResult.Success),
                new AppliedPolicyResult(PolicyB, ConditionalAccessResult.ReportOnlyNotApplied),
            },
            applied);
    }

    [Fact]
    public void Sign_in_without_applied_policies_maps_to_an_empty_list()
    {
        Assert.Empty(GraphDirectoryReader.MapAppliedPolicies(new SignIn()));
        Assert.Empty(GraphDirectoryReader.MapAppliedPolicies(new SignIn { AppliedConditionalAccessPolicies = [] }));
    }

    [Fact]
    public void Applied_policies_without_an_id_are_dropped()
    {
        var applied = GraphDirectoryReader.MapAppliedPolicies(new SignIn
        {
            AppliedConditionalAccessPolicies =
            [
                new AppliedConditionalAccessPolicy { Result = AppliedConditionalAccessPolicyResult.Success },
                new AppliedConditionalAccessPolicy { Id = " ", Result = AppliedConditionalAccessPolicyResult.Failure },
                new AppliedConditionalAccessPolicy { Id = PolicyA },
            ],
        });

        Assert.Equal(new[] { new AppliedPolicyResult(PolicyA, ConditionalAccessResult.Unknown) }, applied);
    }

    [Theory]
    [InlineData(AppliedConditionalAccessPolicyResult.Success, ConditionalAccessResult.Success)]
    [InlineData(AppliedConditionalAccessPolicyResult.Failure, ConditionalAccessResult.Failure)]
    [InlineData(AppliedConditionalAccessPolicyResult.NotApplied, ConditionalAccessResult.NotApplied)]
    [InlineData(AppliedConditionalAccessPolicyResult.NotEnabled, ConditionalAccessResult.NotEnabled)]
    [InlineData(AppliedConditionalAccessPolicyResult.ReportOnlySuccess, ConditionalAccessResult.ReportOnlySuccess)]
    [InlineData(AppliedConditionalAccessPolicyResult.ReportOnlyFailure, ConditionalAccessResult.ReportOnlyFailure)]
    [InlineData(AppliedConditionalAccessPolicyResult.ReportOnlyNotApplied, ConditionalAccessResult.ReportOnlyNotApplied)]
    [InlineData(AppliedConditionalAccessPolicyResult.ReportOnlyInterrupted, ConditionalAccessResult.ReportOnlyInterrupted)]
    [InlineData(AppliedConditionalAccessPolicyResult.Unknown, ConditionalAccessResult.Unknown)]
    [InlineData(AppliedConditionalAccessPolicyResult.UnknownFutureValue, ConditionalAccessResult.Unknown)]
    [InlineData(null, ConditionalAccessResult.Unknown)]
    public void Maps_every_result(AppliedConditionalAccessPolicyResult? result, ConditionalAccessResult expected)
    {
        Assert.Equal(expected, GraphDirectoryReader.MapAppliedPolicyResult(result));
    }

    [Fact]
    public void Filter_asks_for_sign_ins_since_the_start_in_utc()
    {
        var filter = GraphDirectoryReader.SignInsSinceFilter(
            new DateTimeOffset(2026, 10, 18, 4, 0, 0, TimeSpan.FromHours(-7)));

        Assert.Equal("createdDateTime ge 2026-10-18T11:00:00Z", filter);
    }

    [Fact]
    public async Task Maps_a_page_of_sample_sign_ins_into_usage()
    {
        // Trimmed v1.0 sign-ins: one with policy data, one without the property (no Policy.Read.All), one with an empty
        // list, and one with a result Graph might add later.
        var json = $$"""
            {
              "@odata.nextLink": "https://graph.microsoft.com/v1.0/auditLogs/signIns?$top=1000&$skiptoken=abc",
              "value": [
                {
                  "id": "s1",
                  "createdDateTime": "2026-10-24T06:15:41Z",
                  "userPrincipalName": "adele@contoso.example",
                  "status": { "errorCode": 0 },
                  "appliedConditionalAccessPolicies": [
                    { "id": "{{PolicyA}}", "displayName": "Require MFA", "result": "success",
                      "enforcedGrantControls": ["Mfa"], "enforcedSessionControls": [] },
                    { "id": "{{PolicyB}}", "displayName": "Report-only block", "result": "reportOnlyNotApplied",
                      "enforcedGrantControls": [], "enforcedSessionControls": [] }
                  ]
                },
                { "id": "s2", "createdDateTime": "2026-10-24T06:16:00Z", "status": { "errorCode": 50126 } },
                { "id": "s3", "createdDateTime": "2026-10-24T06:17:00Z", "appliedConditionalAccessPolicies": [] },
                {
                  "id": "s4",
                  "createdDateTime": "2026-10-24T06:18:00Z",
                  "appliedConditionalAccessPolicies": [ { "id": "{{PolicyA}}", "result": "someNewResult" } ]
                }
              ]
            }
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var node = await new JsonParseNodeFactory().GetRootParseNodeAsync("application/json", stream);
        var page = node.GetObjectValue(SignInCollectionResponse.CreateFromDiscriminatorValue)!;

        var aggregator = new ConditionalAccessUsageAggregator(default, default);
        foreach (var signIn in page.Value!)
        {
            aggregator.Add(GraphDirectoryReader.MapAppliedPolicies(signIn));
        }

        var usage = aggregator.ToUsage();

        Assert.NotNull(page.OdataNextLink);
        Assert.Equal(4, usage.SignInsRead);
        Assert.Equal(2, usage.SignInsWithPolicyData);
        Assert.Equal(1, usage.Count(PolicyA, ConditionalAccessResult.Success));
        Assert.Equal(1, usage.Count(PolicyA, ConditionalAccessResult.Unknown));
        Assert.Equal(1, usage.Count(PolicyB, ConditionalAccessResult.ReportOnlyNotApplied));
        Assert.Equal(new[] { PolicyA, PolicyB }, usage.ResultsByPolicy.Keys.Order());
    }
}
