using System.Collections.Generic;
using Entraspect.Core.Checks;

namespace Entraspect.Core.Tests.Checks;

public sealed class UsersRiskyCheckTests
{
    private const string UserId = "aaaaaaaa-0000-0000-0000-000000000001";
    private static readonly DateTimeOffset Updated = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);

    private static RiskyUser Risky(
        RiskState state = RiskState.AtRisk,
        RiskLevel level = RiskLevel.High,
        string detail = "",
        bool deleted = false) =>
        new()
        {
            Id = UserId,
            UserPrincipalName = "user@contoso.com",
            DisplayName = "User 1",
            RiskState = state,
            RiskLevel = level,
            RiskDetail = detail,
            RiskLastUpdated = Updated,
            IsDeleted = deleted,
        };

    private static Task<IReadOnlyList<Finding>> RunAsync(params RiskyUser[] users) =>
        new UsersRiskyCheck().RunAsync(new FakeDirectoryReader(apps: []) { RiskyUsers = users }, new CheckOptions());

    [Fact]
    public async Task Confirmed_compromised_is_critical()
    {
        var finding = Assert.Single(await RunAsync(Risky(RiskState.ConfirmedCompromised)));

        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal(
            "Confirmed compromised. Reset the password, revoke sessions and review the account's activity.",
            finding.Message);
        Assert.Equal(("users-risky", "User", UserId), (finding.CheckId, finding.ObjectType, finding.ObjectId));
        Assert.Equal(("User 1", "user@contoso.com"), (finding.ObjectDisplayName, finding.UserPrincipalName));
        Assert.Equal(Updated, finding.DueDate);
        Assert.Equal("High", finding.Value);
    }

    [Theory]
    [InlineData(RiskLevel.High, Severity.Critical, "High")]
    [InlineData(RiskLevel.Medium, Severity.Warning, "Medium")]
    [InlineData(RiskLevel.Low, Severity.Info, "Low")]
    public async Task At_risk_severity_follows_the_level(RiskLevel level, Severity expected, string text)
    {
        var finding = Assert.Single(await RunAsync(Risky(level: level)));

        Assert.Equal(expected, finding.Severity);
        Assert.Equal($"At risk ({text}) according to Microsoft Entra ID Protection.", finding.Message);
        Assert.Equal(text, finding.Value);
    }

    [Theory]
    [InlineData(RiskLevel.Hidden)]
    [InlineData(RiskLevel.None)]
    [InlineData(RiskLevel.Unknown)]
    public async Task At_risk_without_a_level_is_info(RiskLevel level)
    {
        var finding = Assert.Single(await RunAsync(Risky(level: level)));

        Assert.Equal((Severity.Info, "At risk; the risk level isn't shown."), (finding.Severity, finding.Message));
        Assert.Equal(level.ToString(), finding.Value);
    }

    [Theory]
    [InlineData(RiskState.Remediated)]
    [InlineData(RiskState.Dismissed)]
    [InlineData(RiskState.ConfirmedSafe)]
    [InlineData(RiskState.None)]
    [InlineData(RiskState.Unknown)]
    public async Task Other_states_are_not_reported(RiskState state)
    {
        Assert.Empty(await RunAsync(Risky(state)));
    }

    [Fact]
    public async Task Deleted_users_are_ignored()
    {
        Assert.Empty(await RunAsync(Risky(RiskState.ConfirmedCompromised, deleted: true), Risky(deleted: true)));
    }

    [Fact]
    public async Task Risk_detail_is_added()
    {
        var finding = Assert.Single(await RunAsync(Risky(level: RiskLevel.Medium, detail: "userPassedMFADrivenByRiskBasedPolicy")));

        Assert.Equal(
            "At risk (Medium) according to Microsoft Entra ID Protection. Risk detail: userPassedMFADrivenByRiskBasedPolicy.",
            finding.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("none")]
    [InlineData("None")]
    [InlineData("hidden")]
    [InlineData("unknownFutureValue")]
    public async Task Empty_or_uninformative_risk_detail_is_left_out(string detail)
    {
        var finding = Assert.Single(await RunAsync(Risky(level: RiskLevel.Hidden, detail: detail)));

        Assert.Equal("At risk; the risk level isn't shown.", finding.Message);
    }

    [Fact]
    public async Task Missing_risk_date_leaves_the_column_empty()
    {
        var user = new RiskyUser
        {
            Id = UserId,
            UserPrincipalName = "user@contoso.com",
            DisplayName = "User 1",
            RiskState = RiskState.AtRisk,
            RiskLevel = RiskLevel.Low,
        };

        Assert.Null(Assert.Single(await RunAsync(user)).DueDate);
    }

    [Fact]
    public async Task Fails_when_risky_users_cannot_be_read()
    {
        var reader = new FakeDirectoryReader(apps: []) { RiskyUsersFailure = new DirectoryReadException("denied", 403) };

        await Assert.ThrowsAsync<DirectoryReadException>(() => new UsersRiskyCheck().RunAsync(reader, new CheckOptions()));
    }
}
