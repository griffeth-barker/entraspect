using Microsoft.Graph.Models;
using CorePolicyState = Entraspect.Core.ConditionalAccessPolicyState;
using GraphPolicy = Microsoft.Graph.Models.ConditionalAccessPolicy;

namespace Entraspect.Graph.Tests;

// Mapping tests only: they build Graph SDK objects in memory and never create a client or credential.
public sealed class ConditionalAccessMappingTests
{
    private const string UserId = "aaaaaaaa-0000-0000-0000-000000000001";
    private const string GroupId = "99999999-0000-0000-0000-000000000001";
    private const string RoleId = "62e90394-69f5-4237-9190-012177145e10";
    private static readonly DateTimeOffset Created = new(2025, 3, 4, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Modified = new(2026, 1, 2, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Maps_ids_dates_users_groups_roles_and_apps()
    {
        var policy = new GraphPolicy
        {
            Id = "p1",
            DisplayName = "CA001: Require MFA for admins",
            State = ConditionalAccessPolicyState.Enabled,
            CreatedDateTime = Created,
            ModifiedDateTime = Modified,
            Conditions = new ConditionalAccessConditionSet
            {
                Users = new ConditionalAccessUsers
                {
                    IncludeUsers = ["All"],
                    ExcludeUsers = [UserId, "GuestsOrExternalUsers"],
                    IncludeGroups = [GroupId],
                    ExcludeGroups = [""],
                    IncludeRoles = [RoleId],
                },
                Applications = new ConditionalAccessApplications
                {
                    IncludeApplications = ["Office365", "MicrosoftAdminPortals"],
                    IncludeUserActions = ["urn:user:registersecurityinfo"],
                    IncludeAuthenticationContextClassReferences = ["c1"],
                },
            },
        };

        var result = GraphDirectoryReader.MapConditionalAccessPolicy(policy);

        Assert.Equal(("p1", "CA001: Require MFA for admins"), (result.Id, result.DisplayName));
        Assert.Equal(CorePolicyState.Enabled, result.State);
        Assert.Equal((Created, Modified), (result.CreatedDateTime, result.ModifiedDateTime));
        Assert.Equal(new[] { "All" }, result.IncludeUsers);
        Assert.Equal(new[] { UserId, "GuestsOrExternalUsers" }, result.ExcludeUsers);
        Assert.Equal(new[] { GroupId }, result.IncludeGroups);
        Assert.Empty(result.ExcludeGroups);
        Assert.Equal(new[] { RoleId }, result.IncludeRoles);
        Assert.False(result.IncludeGuestsOrExternalUsers);
        Assert.Equal(new[] { "Office365", "MicrosoftAdminPortals" }, result.IncludeApplications);
        Assert.False(result.IncludeApplicationsByFilter);
        Assert.Equal(new[] { "urn:user:registersecurityinfo" }, result.IncludeUserActions);
        Assert.Equal(new[] { "c1" }, result.IncludeAuthenticationContexts);
        Assert.False(result.TargetsServicePrincipals);
    }

    [Theory]
    [InlineData(ConditionalAccessPolicyState.Enabled, CorePolicyState.Enabled)]
    [InlineData(ConditionalAccessPolicyState.Disabled, CorePolicyState.Disabled)]
    [InlineData(ConditionalAccessPolicyState.EnabledForReportingButNotEnforced, CorePolicyState.ReportOnly)]
    public void Maps_the_state(ConditionalAccessPolicyState state, CorePolicyState expected)
    {
        var result = GraphDirectoryReader.MapConditionalAccessPolicy(new GraphPolicy { State = state });

        Assert.Equal(expected, result.State);
    }

    [Fact]
    public void Missing_values_map_to_empty_without_throwing()
    {
        var result = GraphDirectoryReader.MapConditionalAccessPolicy(new GraphPolicy());

        Assert.Equal(string.Empty, result.Id);
        Assert.Equal(string.Empty, result.DisplayName);
        Assert.Equal(CorePolicyState.Enabled, result.State);
        Assert.Null(result.CreatedDateTime);
        Assert.Null(result.ModifiedDateTime);
        Assert.Empty(result.IncludeUsers);
        Assert.Empty(result.IncludeGroups);
        Assert.Empty(result.IncludeApplications);
        Assert.False(result.IncludeGuestsOrExternalUsers);
        Assert.False(result.TargetsServicePrincipals);
    }

    [Fact]
    public void Guest_or_external_user_types_include_guests()
    {
        var result = Map(users: new ConditionalAccessUsers
        {
            IncludeUsers = ["None"],
            IncludeGuestsOrExternalUsers = new ConditionalAccessGuestsOrExternalUsers
            {
                GuestOrExternalUserTypes =
                    ConditionalAccessGuestOrExternalUserTypes.InternalGuest |
                    ConditionalAccessGuestOrExternalUserTypes.B2bCollaborationGuest,
            },
        });

        Assert.True(result.IncludeGuestsOrExternalUsers);
    }

    [Fact]
    public void Guest_object_with_no_types_includes_no_guests()
    {
        var result = Map(users: new ConditionalAccessUsers
        {
            IncludeGuestsOrExternalUsers = new ConditionalAccessGuestsOrExternalUsers
            {
                GuestOrExternalUserTypes = ConditionalAccessGuestOrExternalUserTypes.None,
            },
        });

        Assert.False(result.IncludeGuestsOrExternalUsers);
    }

    [Theory]
    [InlineData("ServicePrincipalsInMyTenant", true)]
    [InlineData("11111111-1111-1111-1111-111111111111", true)]
    [InlineData("None", false)]
    public void Included_service_principals_target_workload_identities(string value, bool expected)
    {
        var result = Map(clientApplications: new ConditionalAccessClientApplications
        {
            IncludeServicePrincipals = [value],
        });

        Assert.Equal(expected, result.TargetsServicePrincipals);
    }

    [Fact]
    public void Include_service_principal_filter_targets_workload_identities()
    {
        var result = Map(clientApplications: new ConditionalAccessClientApplications
        {
            ServicePrincipalFilter = new ConditionalAccessFilter
            {
                Mode = FilterMode.Include,
                Rule = "CustomSecurityAttribute.Workload_Type -eq \"Batch\"",
            },
        });

        Assert.True(result.TargetsServicePrincipals);
    }

    [Theory]
    [InlineData(FilterMode.Include, "CustomSecurityAttribute.App_Tier -eq \"High\"", true)]
    [InlineData(FilterMode.Exclude, "CustomSecurityAttribute.App_Tier -eq \"High\"", false)]
    [InlineData(FilterMode.Include, "", false)]
    public void Include_application_filter_includes_apps(FilterMode mode, string rule, bool expected)
    {
        var result = Map(applications: new ConditionalAccessApplications
        {
            IncludeApplications = ["None"],
            ApplicationFilter = new ConditionalAccessFilter { Mode = mode, Rule = rule },
        });

        Assert.Equal(expected, result.IncludeApplicationsByFilter);
        Assert.Equal(new[] { "None" }, result.IncludeApplications);
    }

    private static Core.ConditionalAccessPolicy Map(
        ConditionalAccessUsers? users = null,
        ConditionalAccessApplications? applications = null,
        ConditionalAccessClientApplications? clientApplications = null) =>
        GraphDirectoryReader.MapConditionalAccessPolicy(new GraphPolicy
        {
            Id = "p1",
            DisplayName = "Policy",
            State = ConditionalAccessPolicyState.Enabled,
            Conditions = new ConditionalAccessConditionSet
            {
                Users = users,
                Applications = applications,
                ClientApplications = clientApplications,
            },
        });
}
