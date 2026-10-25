using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Azure.Identity;
using Entraspect.Core;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;
using AllowInvitesFrom = Entraspect.Core.AllowInvitesFrom;
using ConditionalAccessPolicy = Entraspect.Core.ConditionalAccessPolicy;
using ConditionalAccessPolicyState = Entraspect.Core.ConditionalAccessPolicyState;
using UserType = Entraspect.Core.UserType;

namespace Entraspect.Graph;

public sealed class GraphDirectoryReader : IDirectoryReader
{
    private readonly GraphServiceClient _client;
    private readonly TimeProvider _timeProvider;

    internal GraphDirectoryReader(GraphServiceClient client, TimeProvider? timeProvider = null)
    {
        _client = client;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<IReadOnlyList<AppRegistration>> GetAppRegistrationsAsync(
        CancellationToken cancellationToken = default) =>
        ReadAsync(() => ReadAppRegistrationsAsync(cancellationToken));

    public Task<IReadOnlyList<DirectoryGroup>> GetGroupsAsync(CancellationToken cancellationToken = default) =>
        ReadAsync(() => ReadGroupsAsync(cancellationToken));

    public Task<IReadOnlyList<string>> GetGroupIdsWithLicenseErrorsAsync(CancellationToken cancellationToken = default) =>
        ReadAsync(() => ReadGroupIdsWithLicenseErrorsAsync(cancellationToken));

    public Task<IReadOnlyList<EnterpriseApp>> GetEnterpriseAppsAsync(CancellationToken cancellationToken = default) =>
        ReadAsync(() => ReadEnterpriseAppsAsync(cancellationToken));

    public Task<IReadOnlyList<UserRegistration>> GetUserRegistrationsAsync(
        CancellationToken cancellationToken = default) =>
        ReadAsync(() => ReadUserRegistrationsAsync(cancellationToken));

    public Task<IReadOnlyList<AppPermissionGrant>> GetGraphApplicationPermissionGrantsAsync(
        CancellationToken cancellationToken = default) =>
        ReadAsync(() => ReadGraphApplicationPermissionGrantsAsync(cancellationToken));

    public Task<IReadOnlyList<ConditionalAccessPolicy>> GetConditionalAccessPoliciesAsync(
        CancellationToken cancellationToken = default) =>
        ReadAsync(() => ReadConditionalAccessPoliciesAsync(cancellationToken));

    public Task<IReadOnlyList<string>> GetMissingUserIdsAsync(
        IReadOnlyCollection<string> userIds,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default) =>
        ReadAsync(() => ReadMissingUserIdsAsync(userIds, progress, cancellationToken));

    public Task<IReadOnlyList<DirectoryUser>> GetUsersAsync(CancellationToken cancellationToken = default) =>
        ReadAsync(() => ReadUsersAsync(cancellationToken));

    public Task<IReadOnlyList<DirectoryRoleAssignment>> GetDirectoryRoleAssignmentsAsync(
        CancellationToken cancellationToken = default) =>
        ReadAsync(() => ReadDirectoryRoleAssignmentsAsync(cancellationToken));

    public Task<IReadOnlyList<string>> GetGroupTransitiveUserIdsAsync(
        string groupId,
        CancellationToken cancellationToken = default) =>
        ReadAsync(() => ReadGroupTransitiveUserIdsAsync(groupId, cancellationToken));

    public Task<bool> GetSecurityDefaultsEnabledAsync(CancellationToken cancellationToken = default) =>
        ReadAsync(() => ReadSecurityDefaultsEnabledAsync(cancellationToken));

    public Task<PerUserMfaState> GetPerUserMfaStateAsync(
        string userId,
        CancellationToken cancellationToken = default) =>
        ReadAsync(() => ReadPerUserMfaStateAsync(userId, cancellationToken));

    public Task<IReadOnlyDictionary<string, SingleFactorSignIns>> GetSingleFactorSignInsAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default) =>
        ReadAsync(() => ReadSingleFactorSignInsAsync(since, cancellationToken));

    public Task<ConditionalAccessUsage> GetConditionalAccessUsageAsync(
        DateTimeOffset since,
        IProgress<int>? signInsRead = null,
        CancellationToken cancellationToken = default) =>
        ReadAsync(() => ReadConditionalAccessUsageAsync(since, signInsRead, cancellationToken));

    public Task<AuthorizationPolicySettings> GetAuthorizationPolicyAsync(CancellationToken cancellationToken = default) =>
        ReadAsync(() => ReadAuthorizationPolicyAsync(cancellationToken));

    public Task<CrossTenantAccessSettings> GetCrossTenantAccessDefaultsAsync(
        CancellationToken cancellationToken = default) =>
        ReadAsync(() => ReadCrossTenantAccessDefaultsAsync(cancellationToken));

    public Task<IReadOnlyList<CrossTenantAccessSettings>> GetCrossTenantAccessPartnersAsync(
        CancellationToken cancellationToken = default) =>
        ReadAsync(() => ReadCrossTenantAccessPartnersAsync(cancellationToken));

    /// <summary>The application (client) ID of Microsoft Graph, the same in every tenant.</summary>
    public const string MicrosoftGraphAppId = "00000003-0000-0000-c000-000000000000";

    // Turns Graph SDK and Azure.Identity failures into DirectoryReadException, so no SDK types leave this assembly.
    private static async Task<T> ReadAsync<T>(Func<Task<T>> read)
    {
        try
        {
            return await read().ConfigureAwait(false);
        }
        catch (ApiException ex)
        {
            throw ToDirectoryReadException(ex);
        }
        catch (AuthenticationFailedException ex)
        {
            throw new DirectoryReadException($"Sign-in failed: {ex.Message}", innerException: ex);
        }
        catch (JsonException ex)
        {
            // Only raw JSON responses (beta, and the authorization policy) are parsed here; a response that isn't JSON
            // fails just the check that read it.
            throw new DirectoryReadException(
                $"Microsoft Graph returned a response that couldn't be read: {ex.Message}",
                innerException: ex);
        }
    }

    /// <summary>The Graph error as a <see cref="DirectoryReadException"/>, with the OData error code when given.</summary>
    public static DirectoryReadException ToDirectoryReadException(ApiException ex)
    {
        var error = (ex as ODataError)?.Error;
        var detail = error?.Message is { Length: > 0 } message ? message : ex.Message;
        return new DirectoryReadException(
            $"Microsoft Graph returned HTTP {ex.ResponseStatusCode}: {detail}",
            ex.ResponseStatusCode,
            ex,
            string.IsNullOrEmpty(error?.Code) ? null : error.Code);
    }

    /// <summary>
    /// Reads every application, then which have at least one owner from a second listing that expands
    /// <c>owners($select=id)</c>, as for groups: an expanded relationship returns up to 20 items, enough to tell
    /// empty from not empty. The owners listing is separate so the first listing keeps its 999 page size; selecting
    /// <c>keyCredentials</c> is throttled to 150 requests a minute per tenant. Never uses <c>POST /$batch</c>.
    /// </summary>
    private async Task<IReadOnlyList<AppRegistration>> ReadAppRegistrationsAsync(CancellationToken cancellationToken)
    {
        var applications = await ListApplicationsAsync(config =>
        {
            config.QueryParameters.Select =
            [
                "id", "appId", "displayName", "passwordCredentials", "keyCredentials", "signInAudience", "web",
                "spa", "publicClient", "identifierUris",
            ];
            config.QueryParameters.Top = 999;
        }, cancellationToken).ConfigureAwait(false);

        var owned = await ListApplicationsAsync(config =>
        {
            config.QueryParameters.Select = ["id"];
            config.QueryParameters.Expand = ["owners($select=id)"];
            // Graph may return smaller pages when expanding; every page is followed either way.
            config.QueryParameters.Top = 100;
        }, cancellationToken).ConfigureAwait(false);

        var withOwners = owned
            .Where(a => a.Id is not null && a.Owners is { Count: > 0 })
            .Select(a => a.Id!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return applications
            .Select(a => MapApplication(a, withOwners.Contains(a.Id ?? string.Empty)))
            .ToList();
    }

    private async Task<List<Application>> ListApplicationsAsync(
        Action<RequestConfiguration<Microsoft.Graph.Applications.ApplicationsRequestBuilder.ApplicationsRequestBuilderGetQueryParameters>> configure,
        CancellationToken cancellationToken)
    {
        var result = new List<Application>();

        ApplicationCollectionResponse? response =
            await _client.Applications.GetAsync(configure, cancellationToken).ConfigureAwait(false);

        while (response is not null)
        {
            if (response.Value is not null)
            {
                result.AddRange(response.Value);
            }

            response = string.IsNullOrEmpty(response.OdataNextLink)
                ? null
                : await _client.Applications.WithUrl(response.OdataNextLink)
                    .GetAsync(_ => { }, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>
    /// Every Microsoft Graph application permission granted in the tenant. Reads the Microsoft Graph service
    /// principal (<c>GET /servicePrincipals(appId='00000003-0000-0000-c000-000000000000')</c>) for its app roles,
    /// which map role IDs to permission names, then lists
    /// <c>GET /servicePrincipals/{id}/appRoleAssignedTo</c>, following every page: one listing with every app role
    /// assignment on Microsoft Graph instead of one call per app. The docs list no <c>$top</c> for it, so Graph's
    /// default page size is used. Assignments to users and groups are skipped. A tenant without the Microsoft Graph
    /// service principal has no grants.
    /// </summary>
    private async Task<IReadOnlyList<AppPermissionGrant>> ReadGraphApplicationPermissionGrantsAsync(
        CancellationToken cancellationToken)
    {
        ServicePrincipal? graph;
        try
        {
            graph = await _client.ServicePrincipalsWithAppId(MicrosoftGraphAppId).GetAsync(config =>
            {
                config.QueryParameters.Select = ["id", "appRoles"];
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (ApiException ex) when (ex.ResponseStatusCode == 404)
        {
            return [];
        }

        if (graph?.Id is not { Length: > 0 } graphId)
        {
            return [];
        }

        var roleNames = MapAppRoleNames(graph.AppRoles);
        var requests = _client.ServicePrincipals[graphId].AppRoleAssignedTo;
        var result = new List<AppPermissionGrant>();

        AppRoleAssignmentCollectionResponse? response = await requests.GetAsync(config =>
        {
            config.QueryParameters.Select =
            [
                "id", "appRoleId", "createdDateTime", "principalDisplayName", "principalId", "principalType",
            ];
        }, cancellationToken).ConfigureAwait(false);

        while (response is not null)
        {
            if (response.Value is not null)
            {
                foreach (var assignment in response.Value)
                {
                    if (MapAppRoleAssignment(assignment, roleNames) is { } grant)
                    {
                        result.Add(grant);
                    }
                }
            }

            response = string.IsNullOrEmpty(response.OdataNextLink)
                ? null
                : await requests.WithUrl(response.OdataNextLink)
                    .GetAsync(_ => { }, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>
    /// <c>GET /identity/conditionalAccess/policies</c>, following every page. Graph's default page size is used.
    /// </summary>
    private async Task<IReadOnlyList<ConditionalAccessPolicy>> ReadConditionalAccessPoliciesAsync(
        CancellationToken cancellationToken)
    {
        var result = new List<ConditionalAccessPolicy>();
        var requests = _client.Identity.ConditionalAccess.Policies;

        ConditionalAccessPolicyCollectionResponse? response = await requests.GetAsync(config =>
        {
            config.QueryParameters.Select =
            [
                "id", "displayName", "state", "createdDateTime", "modifiedDateTime", "conditions", "grantControls",
            ];
        }, cancellationToken).ConfigureAwait(false);

        while (response is not null)
        {
            if (response.Value is not null)
            {
                result.AddRange(response.Value.Select(MapConditionalAccessPolicy));
            }

            response = string.IsNullOrEmpty(response.OdataNextLink)
                ? null
                : await requests.WithUrl(response.OdataNextLink)
                    .GetAsync(_ => { }, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>
    /// Maps a Graph policy defensively: missing lists are empty, blank entries are dropped, and a missing state counts
    /// as enabled so the policy's other rules still apply. A missing grant operator counts as <c>OR</c>, so another
    /// control never makes a policy look stricter than it is.
    /// </summary>
    public static ConditionalAccessPolicy MapConditionalAccessPolicy(Microsoft.Graph.Models.ConditionalAccessPolicy policy)
    {
        var users = policy.Conditions?.Users;
        var applications = policy.Conditions?.Applications;
        var clientApplications = policy.Conditions?.ClientApplications;
        var grant = policy.GrantControls;

        return new ConditionalAccessPolicy
        {
            Id = policy.Id ?? string.Empty,
            DisplayName = policy.DisplayName ?? string.Empty,
            State = policy.State switch
            {
                Microsoft.Graph.Models.ConditionalAccessPolicyState.Disabled => ConditionalAccessPolicyState.Disabled,
                Microsoft.Graph.Models.ConditionalAccessPolicyState.EnabledForReportingButNotEnforced =>
                    ConditionalAccessPolicyState.ReportOnly,
                _ => ConditionalAccessPolicyState.Enabled,
            },
            CreatedDateTime = policy.CreatedDateTime,
            ModifiedDateTime = policy.ModifiedDateTime,
            IncludeUsers = Values(users?.IncludeUsers),
            ExcludeUsers = Values(users?.ExcludeUsers),
            IncludeGroups = Values(users?.IncludeGroups),
            ExcludeGroups = Values(users?.ExcludeGroups),
            IncludeRoles = Values(users?.IncludeRoles),
            ExcludeRoles = Values(users?.ExcludeRoles),
            IncludeGuestTypes = GuestTypes(users?.IncludeGuestsOrExternalUsers),
            IncludeGuestsLimitedToTenants = IsLimitedToTenants(users?.IncludeGuestsOrExternalUsers),
            ExcludeGuestTypes = GuestTypes(users?.ExcludeGuestsOrExternalUsers),
            ExcludeGuestsLimitedToTenants = IsLimitedToTenants(users?.ExcludeGuestsOrExternalUsers),
            IncludeApplications = Values(applications?.IncludeApplications),
            IncludeApplicationsByFilter = IsIncludeFilter(applications?.ApplicationFilter),
            ExcludeApplications = Values(applications?.ExcludeApplications),
            ExcludeApplicationsByFilter = IsFilter(applications?.ApplicationFilter, FilterMode.Exclude),
            IncludeUserActions = Values(applications?.IncludeUserActions),
            IncludeAuthenticationContexts = Values(applications?.IncludeAuthenticationContextClassReferences),
            TargetsServicePrincipals =
                Values(clientApplications?.IncludeServicePrincipals)
                    .Any(v => !string.Equals(v, "None", StringComparison.OrdinalIgnoreCase)) ||
                IsIncludeFilter(clientApplications?.ServicePrincipalFilter),
            GrantBuiltInControls = grant?.BuiltInControls?
                .Where(c => c is not null)
                .Select(c => GrantControlName(c!.Value))
                .ToList() ?? [],
            GrantOperator = string.Equals(grant?.Operator, "AND", StringComparison.OrdinalIgnoreCase)
                ? GrantOperator.And
                : GrantOperator.Or,
            HasAuthenticationStrength = HasAuthenticationStrength(grant),
            HasNarrowingConditions = HasNarrowingConditions(policy.Conditions),
        };
    }

    /// <summary>The control as Graph spells it, for example <c>mfa</c> or <c>compliantDevice</c>.</summary>
    private static string GrantControlName(ConditionalAccessGrantControl control)
    {
        var name = control.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    /// <summary>Whether the grant needs an authentication strength; Graph returns the strength object with the policy.</summary>
    private static bool HasAuthenticationStrength(ConditionalAccessGrantControls? grant) =>
        grant?.AuthenticationStrength is { } strength &&
        (!string.IsNullOrEmpty(strength.Id) || !string.IsNullOrEmpty(strength.DisplayName));

    /// <summary>
    /// Whether the policy applies only under some conditions: locations or device platforms other than all, any user,
    /// sign-in or insider risk level, a device filter, authentication flows, or client app types other than all (a
    /// set with both browser and mobile apps and desktop clients counts as all).
    /// </summary>
    public static bool HasNarrowingConditions(ConditionalAccessConditionSet? conditions)
    {
        if (conditions is null)
        {
            return false;
        }

        var locations = conditions.Locations;
        if (!IsAllOrEmpty(Values(locations?.IncludeLocations)) || Values(locations?.ExcludeLocations).Count > 0)
        {
            return true;
        }

        var platforms = conditions.Platforms;
        var includePlatforms = platforms?.IncludePlatforms?.Where(p => p is not null).Select(p => p!.Value).ToList() ?? [];
        if ((includePlatforms.Count > 0 && !includePlatforms.Contains(ConditionalAccessDevicePlatform.All)) ||
            platforms?.ExcludePlatforms?.Any(p => p is not null) == true)
        {
            return true;
        }

        if (conditions.UserRiskLevels?.Any(l => l is not null) == true ||
            conditions.SignInRiskLevels?.Any(l => l is not null) == true ||
            conditions.InsiderRiskLevels is not null ||
            IsFilter(conditions.Devices?.DeviceFilter, mode: null) ||
            conditions.AuthenticationFlows?.TransferMethods is { } methods && methods != ConditionalAccessTransferMethods.None)
        {
            return true;
        }

        var clientApps = conditions.ClientAppTypes?.Where(c => c is not null).Select(c => c!.Value).ToList() ?? [];
        return clientApps.Count > 0 &&
            !clientApps.Contains(ConditionalAccessClientApp.All) &&
            !(clientApps.Contains(ConditionalAccessClientApp.Browser) &&
                clientApps.Contains(ConditionalAccessClientApp.MobileAppsAndDesktopClients));
    }

    private static bool IsAllOrEmpty(IReadOnlyList<string> values) =>
        values.All(v => string.Equals(v, "All", StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether guest settings apply only to listed external tenants rather than to all of them.</summary>
    private static bool IsLimitedToTenants(ConditionalAccessGuestsOrExternalUsers? guests) =>
        guests?.ExternalTenants is { } tenants &&
        (tenants is ConditionalAccessEnumeratedExternalTenants ||
            tenants.MembershipKind == ConditionalAccessExternalTenantsMembershipKind.Enumerated);

    /// <summary>
    /// The guest or external user types, mapped flag by flag; a missing object or type list, or an unknown future
    /// value, adds nothing.
    /// </summary>
    public static GuestOrExternalUserTypes GuestTypes(ConditionalAccessGuestsOrExternalUsers? guests)
    {
        var types = guests?.GuestOrExternalUserTypes ?? ConditionalAccessGuestOrExternalUserTypes.None;
        var result = GuestOrExternalUserTypes.None;

        foreach (var (graph, core) in GuestTypeMap)
        {
            if (types.HasFlag(graph))
            {
                result |= core;
            }
        }

        return result;
    }

    private static readonly (ConditionalAccessGuestOrExternalUserTypes Graph, GuestOrExternalUserTypes Core)[] GuestTypeMap =
    [
        (ConditionalAccessGuestOrExternalUserTypes.InternalGuest, GuestOrExternalUserTypes.InternalGuest),
        (ConditionalAccessGuestOrExternalUserTypes.B2bCollaborationGuest, GuestOrExternalUserTypes.B2bCollaborationGuest),
        (ConditionalAccessGuestOrExternalUserTypes.B2bCollaborationMember, GuestOrExternalUserTypes.B2bCollaborationMember),
        (ConditionalAccessGuestOrExternalUserTypes.B2bDirectConnectUser, GuestOrExternalUserTypes.B2bDirectConnectUser),
        (ConditionalAccessGuestOrExternalUserTypes.OtherExternalUser, GuestOrExternalUserTypes.OtherExternalUser),
        (ConditionalAccessGuestOrExternalUserTypes.ServiceProvider, GuestOrExternalUserTypes.ServiceProvider),
    ];

    private static bool IsIncludeFilter(ConditionalAccessFilter? filter) => IsFilter(filter, FilterMode.Include);

    /// <summary>Whether the filter has a rule and, unless <paramref name="mode"/> is null, that mode.</summary>
    private static bool IsFilter(ConditionalAccessFilter? filter, FilterMode? mode) =>
        filter is not null && (mode is null || filter.Mode == mode) && !string.IsNullOrWhiteSpace(filter.Rule);

    private static List<string> Values(IEnumerable<string>? values) =>
        values?.Where(v => !string.IsNullOrWhiteSpace(v)).ToList() ?? [];

    /// <summary>
    /// <c>GET /users/{id}?$select=id</c> for each distinct ID, one after another; a 404 means the user doesn't exist.
    /// Any other error fails the read. Never uses <c>POST /directoryObjects/getByIds</c> or <c>POST /$batch</c>.
    /// </summary>
    private async Task<IReadOnlyList<string>> ReadMissingUserIdsAsync(
        IReadOnlyCollection<string> userIds,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var missing = new List<string>();
        var done = 0;

        foreach (var id in userIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                await _client.Users[id].GetAsync(config =>
                {
                    config.QueryParameters.Select = ["id"];
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (ApiException ex) when (ex.ResponseStatusCode == 404)
            {
                missing.Add(id);
            }

            progress?.Report(++done);
        }

        return missing;
    }

    /// <summary><c>GET /users?$select=id,displayName,userPrincipalName,userType,accountEnabled&amp;$top=999</c>, following every page.</summary>
    private async Task<IReadOnlyList<DirectoryUser>> ReadUsersAsync(CancellationToken cancellationToken)
    {
        var result = new List<DirectoryUser>();

        UserCollectionResponse? response = await _client.Users.GetAsync(config =>
        {
            config.QueryParameters.Select = ["id", "displayName", "userPrincipalName", "userType", "accountEnabled"];
            config.QueryParameters.Top = 999;
        }, cancellationToken).ConfigureAwait(false);

        while (response is not null)
        {
            if (response.Value is not null)
            {
                result.AddRange(response.Value.Where(u => !string.IsNullOrEmpty(u.Id)).Select(MapUser));
            }

            response = string.IsNullOrEmpty(response.OdataNextLink)
                ? null
                : await _client.Users.WithUrl(response.OdataNextLink)
                    .GetAsync(_ => { }, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    public static DirectoryUser MapUser(User user) =>
        new()
        {
            Id = user.Id ?? string.Empty,
            UserPrincipalName = user.UserPrincipalName ?? string.Empty,
            DisplayName = user.DisplayName ?? string.Empty,
            // Anything but guest, including a missing value, counts as a member so it isn't played down.
            UserType = string.Equals(user.UserType, "Guest", StringComparison.OrdinalIgnoreCase)
                ? UserType.Guest
                : UserType.Member,
            // Missing means enabled, so the user is checked rather than silently skipped.
            AccountEnabled = user.AccountEnabled ?? true,
        };

    /// <summary>
    /// <c>GET /roleManagement/directory/roleAssignments?$select=roleDefinitionId,principalId</c>, following every
    /// page. Lists only active assignments; PIM-eligible ones are a separate resource.
    /// </summary>
    private async Task<IReadOnlyList<DirectoryRoleAssignment>> ReadDirectoryRoleAssignmentsAsync(
        CancellationToken cancellationToken)
    {
        var result = new List<DirectoryRoleAssignment>();
        var requests = _client.RoleManagement.Directory.RoleAssignments;

        UnifiedRoleAssignmentCollectionResponse? response = await requests.GetAsync(config =>
        {
            config.QueryParameters.Select = ["roleDefinitionId", "principalId"];
        }, cancellationToken).ConfigureAwait(false);

        while (response is not null)
        {
            if (response.Value is not null)
            {
                foreach (var assignment in response.Value)
                {
                    if (MapRoleAssignment(assignment) is { } mapped)
                    {
                        result.Add(mapped);
                    }
                }
            }

            response = string.IsNullOrEmpty(response.OdataNextLink)
                ? null
                : await requests.WithUrl(response.OdataNextLink)
                    .GetAsync(_ => { }, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>The assignment, or null when Graph leaves out the role or the principal.</summary>
    public static DirectoryRoleAssignment? MapRoleAssignment(UnifiedRoleAssignment assignment) =>
        string.IsNullOrEmpty(assignment.RoleDefinitionId) || string.IsNullOrEmpty(assignment.PrincipalId)
            ? null
            : new DirectoryRoleAssignment
            {
                RoleDefinitionId = assignment.RoleDefinitionId,
                PrincipalId = assignment.PrincipalId,
            };

    /// <summary>
    /// <c>GET /groups/{id}/transitiveMembers/microsoft.graph.user?$select=id&amp;$top=999</c>, following every page.
    /// The OData cast is an advanced query, so it sends <c>ConsistencyLevel: eventual</c> and <c>$count=true</c> as the
    /// docs require. A 404 (the ID isn't a group, or the group was deleted) means no members.
    /// </summary>
    private async Task<IReadOnlyList<string>> ReadGroupTransitiveUserIdsAsync(
        string groupId,
        CancellationToken cancellationToken)
    {
        var result = new List<string>();
        var requests = _client.Groups[groupId].TransitiveMembers.GraphUser;
        UserCollectionResponse? response;

        try
        {
            response = await requests.GetAsync(config =>
            {
                config.QueryParameters.Select = ["id"];
                config.QueryParameters.Top = 999;
                config.QueryParameters.Count = true;
                config.Headers.Add("ConsistencyLevel", "eventual");
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (ApiException ex) when (ex.ResponseStatusCode == 404)
        {
            return [];
        }

        while (response is not null)
        {
            if (response.Value is not null)
            {
                result.AddRange(response.Value.Select(u => u.Id).OfType<string>().Where(id => id.Length > 0));
            }

            response = string.IsNullOrEmpty(response.OdataNextLink)
                ? null
                : await requests.WithUrl(response.OdataNextLink)
                    .GetAsync(config => config.Headers.Add("ConsistencyLevel", "eventual"), cancellationToken)
                    .ConfigureAwait(false);
        }

        return result;
    }

    /// <summary><c>GET /policies/identitySecurityDefaultsEnforcementPolicy?$select=isEnabled</c>.</summary>
    private async Task<bool> ReadSecurityDefaultsEnabledAsync(CancellationToken cancellationToken)
    {
        var policy = await _client.Policies.IdentitySecurityDefaultsEnforcementPolicy.GetAsync(config =>
        {
            config.QueryParameters.Select = ["isEnabled"];
        }, cancellationToken).ConfigureAwait(false);

        return policy?.IsEnabled == true;
    }

    /// <summary>The Microsoft Graph v1.0 endpoint.</summary>
    public const string V1BaseUrl = "https://graph.microsoft.com/v1.0";

    /// <summary>
    /// <c>GET /policies/authorizationPolicy?$select=guestUserRoleId,allowInvitesFrom,allowEmailVerifiedUsersToJoinOrganization</c>,
    /// read as JSON: the SDK's types drop an <c>allowInvitesFrom</c> they don't know and a <c>guestUserRoleId</c> that
    /// isn't a GUID, and the check shows those raw values.
    /// </summary>
    private async Task<AuthorizationPolicySettings> ReadAuthorizationPolicyAsync(CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(
            $"{V1BaseUrl}/policies/authorizationPolicy" +
            "?$select=guestUserRoleId,allowInvitesFrom,allowEmailVerifiedUsersToJoinOrganization",
            cancellationToken).ConfigureAwait(false);
        return MapAuthorizationPolicy(document.RootElement);
    }

    /// <summary>Missing or unexpected values never throw.</summary>
    public static AuthorizationPolicySettings MapAuthorizationPolicy(JsonElement policy)
    {
        var allowInvitesFrom = String(policy, "allowInvitesFrom");
        return new AuthorizationPolicySettings
        {
            GuestUserRoleId = String(policy, "guestUserRoleId")?.Trim() ?? string.Empty,
            AllowInvitesFrom = MapAllowInvitesFrom(allowInvitesFrom),
            AllowInvitesFromRaw = allowInvitesFrom,
            AllowEmailVerifiedUsersToJoinOrganization = Boolean(policy, "allowEmailVerifiedUsersToJoinOrganization"),
        };
    }

    /// <summary>A missing or unrecognised value is <see cref="AllowInvitesFrom.Unknown"/>; case is ignored.</summary>
    public static AllowInvitesFrom MapAllowInvitesFrom(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "NONE" => AllowInvitesFrom.None,
        "ADMINSANDGUESTINVITERS" => AllowInvitesFrom.AdminsAndGuestInviters,
        "ADMINSGUESTINVITERSANDALLMEMBERS" => AllowInvitesFrom.AdminsGuestInvitersAndAllMembers,
        "EVERYONE" => AllowInvitesFrom.Everyone,
        _ => AllowInvitesFrom.Unknown,
    };

    /// <summary><c>GET /policies/crossTenantAccessPolicy/default</c>.</summary>
    private async Task<CrossTenantAccessSettings> ReadCrossTenantAccessDefaultsAsync(CancellationToken cancellationToken)
    {
        var defaults = await _client.Policies.CrossTenantAccessPolicy.Default
            .GetAsync(_ => { }, cancellationToken).ConfigureAwait(false);

        return MapCrossTenantAccessDefaults(defaults);
    }

    /// <summary><c>GET /policies/crossTenantAccessPolicy/partners</c>, following every page.</summary>
    private async Task<IReadOnlyList<CrossTenantAccessSettings>> ReadCrossTenantAccessPartnersAsync(
        CancellationToken cancellationToken)
    {
        var result = new List<CrossTenantAccessSettings>();
        var partners = _client.Policies.CrossTenantAccessPolicy.Partners;

        var response = await partners.GetAsync(_ => { }, cancellationToken).ConfigureAwait(false);

        while (response is not null)
        {
            if (response.Value is not null)
            {
                result.AddRange(response.Value.OfType<CrossTenantAccessPolicyConfigurationPartner>()
                    .Select(MapCrossTenantAccessPartner));
            }

            response = string.IsNullOrEmpty(response.OdataNextLink)
                ? null
                : await partners.WithUrl(response.OdataNextLink)
                    .GetAsync(_ => { }, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    public static CrossTenantAccessSettings MapCrossTenantAccessDefaults(
        CrossTenantAccessPolicyConfigurationDefault? defaults) => new()
    {
        IsServiceDefault = defaults?.IsServiceDefault,
        InboundTrust = MapInboundTrust(defaults?.InboundTrust),
        AutomaticRedemptionInbound = defaults?.AutomaticUserConsentSettings?.InboundAllowed,
        AutomaticRedemptionOutbound = defaults?.AutomaticUserConsentSettings?.OutboundAllowed,
        B2bCollaborationInbound = MapCrossTenantAccess(defaults?.B2bCollaborationInbound),
        B2bCollaborationOutbound = MapCrossTenantAccess(defaults?.B2bCollaborationOutbound),
        B2bDirectConnectInbound = MapCrossTenantAccess(defaults?.B2bDirectConnectInbound),
        B2bDirectConnectOutbound = MapCrossTenantAccess(defaults?.B2bDirectConnectOutbound),
    };

    /// <summary>A partner's settings. A property Graph returns as null isn't set and inherits the defaults.</summary>
    public static CrossTenantAccessSettings MapCrossTenantAccessPartner(CrossTenantAccessPolicyConfigurationPartner partner) =>
        new()
        {
            TenantId = partner.TenantId ?? string.Empty,
            IsServiceProvider = partner.IsServiceProvider,
            InboundTrust = MapInboundTrust(partner.InboundTrust),
            AutomaticRedemptionInbound = partner.AutomaticUserConsentSettings?.InboundAllowed,
            AutomaticRedemptionOutbound = partner.AutomaticUserConsentSettings?.OutboundAllowed,
            B2bCollaborationInbound = MapCrossTenantAccess(partner.B2bCollaborationInbound),
            B2bCollaborationOutbound = MapCrossTenantAccess(partner.B2bCollaborationOutbound),
            B2bDirectConnectInbound = MapCrossTenantAccess(partner.B2bDirectConnectInbound),
            B2bDirectConnectOutbound = MapCrossTenantAccess(partner.B2bDirectConnectOutbound),
        };

    private static CrossTenantInboundTrust MapInboundTrust(CrossTenantAccessPolicyInboundTrust? trust) => new()
    {
        IsMfaAccepted = trust?.IsMfaAccepted,
        IsCompliantDeviceAccepted = trust?.IsCompliantDeviceAccepted,
        IsHybridJoinedDeviceAccepted = trust?.IsHybridAzureADJoinedDeviceAccepted,
    };

    /// <summary>The <c>AllUsers</c> target of <c>usersAndGroups</c>.</summary>
    public const string AllUsersTarget = "AllUsers";

    /// <summary>The <c>AllApplications</c> target of <c>applications</c>. <c>Office365</c> isn't all applications.</summary>
    public const string AllApplicationsTarget = "AllApplications";

    /// <summary>Null when Graph returned no setting (on a partner: inherited from the defaults).</summary>
    public static CrossTenantAccess? MapCrossTenantAccess(CrossTenantAccessPolicyB2BSetting? setting) =>
        setting is null
            ? null
            : new CrossTenantAccess
            {
                Users = MapAccessTarget(setting.UsersAndGroups, AllUsersTarget),
                Applications = MapAccessTarget(setting.Applications, AllApplicationsTarget),
            };

    /// <summary>
    /// Null when Graph returned no configuration. A missing or unrecognised access type is
    /// <see cref="CrossTenantAccessType.Unknown"/>; missing targets don't include all.
    /// </summary>
    public static CrossTenantAccessTarget? MapAccessTarget(
        CrossTenantAccessPolicyTargetConfiguration? configuration,
        string allTarget) =>
        configuration is null
            ? null
            : new CrossTenantAccessTarget(
                configuration.AccessType switch
                {
                    CrossTenantAccessPolicyTargetConfigurationAccessType.Allowed => CrossTenantAccessType.Allowed,
                    CrossTenantAccessPolicyTargetConfigurationAccessType.Blocked => CrossTenantAccessType.Blocked,
                    _ => CrossTenantAccessType.Unknown,
                },
                configuration.Targets?.Any(t =>
                    string.Equals(t?.Target, allTarget, StringComparison.OrdinalIgnoreCase)) == true);

    /// <summary>The Microsoft Graph beta endpoint, for data that v1.0 doesn't have.</summary>
    public const string BetaBaseUrl = "https://graph.microsoft.com/beta";

    /// <summary>
    /// Beta: <c>GET /beta/users/{id}/authentication/requirements</c>, reading <c>perUserMfaState</c>; v1.0 has no
    /// per-user MFA state. A 404 (the user was deleted since it was listed) is unknown.
    /// </summary>
    private async Task<PerUserMfaState> ReadPerUserMfaStateAsync(string userId, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await GetJsonAsync(
                $"{BetaBaseUrl}/users/{Uri.EscapeDataString(userId)}/authentication/requirements",
                cancellationToken).ConfigureAwait(false);
            return MapPerUserMfaState(document.RootElement);
        }
        catch (ApiException ex) when (ex.ResponseStatusCode == 404)
        {
            return PerUserMfaState.Unknown;
        }
    }

    /// <summary>A missing, non-text or unrecognised <c>perUserMfaState</c> is unknown.</summary>
    public static PerUserMfaState MapPerUserMfaState(JsonElement requirements) =>
        requirements.ValueKind == JsonValueKind.Object &&
        requirements.TryGetProperty("perUserMfaState", out var state) &&
        state.ValueKind == JsonValueKind.String
            ? state.GetString()?.ToLowerInvariant() switch
            {
                "disabled" => PerUserMfaState.Disabled,
                "enabled" => PerUserMfaState.Enabled,
                "enforced" => PerUserMfaState.Enforced,
                _ => PerUserMfaState.Unknown,
            }
            : PerUserMfaState.Unknown;

    /// <summary>The <c>authenticationRequirement</c> of a sign-in that used only one factor.</summary>
    public const string SingleFactorAuthentication = "singleFactorAuthentication";

    /// <summary>
    /// Beta: <c>GET /beta/auditLogs/signIns</c>, because v1.0 sign-ins have no <c>authenticationRequirement</c>. The
    /// beta docs list <c>$filter</c> (<c>eq</c>) on <c>authenticationRequirement</c> and <c>status/errorCode</c>, so
    /// only successful single-factor sign-ins since <paramref name="since"/> are asked for, 1,000 a page; without a
    /// <c>signInEventTypes</c> filter Graph returns interactive sign-ins only. Each page is added to the totals and
    /// dropped before the next is read, and cancellation is checked between pages.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, SingleFactorSignIns>> ReadSingleFactorSignInsAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        var totals = new Dictionary<string, SingleFactorSignIns>(StringComparer.OrdinalIgnoreCase);
        var url = SingleFactorSignInsUrl(since);

        while (url is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var page = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
            url = AddSingleFactorSignIns(page.RootElement, totals);
        }

        return totals;
    }

    public static string SingleFactorSignInsUrl(DateTimeOffset since)
    {
        var filter =
            $"createdDateTime ge {since.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ} and " +
            $"authenticationRequirement eq '{SingleFactorAuthentication}' and status/errorCode eq 0";
        return $"{BetaBaseUrl}/auditLogs/signIns?$filter={Uri.EscapeDataString(filter)}&$top=1000" +
            "&$select=userId,createdDateTime,authenticationRequirement,status";
    }

    /// <summary>
    /// Adds one page of sign-ins to <paramref name="totals"/> and returns the next page's link, or null on the last
    /// page. The server filter is checked again, so only successful single-factor sign-ins count. A sign-in without a
    /// user ID or a readable date is skipped; missing or unexpected values never throw.
    /// </summary>
    public static string? AddSingleFactorSignIns(JsonElement page, IDictionary<string, SingleFactorSignIns> totals)
    {
        if (page.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (page.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array)
        {
            foreach (var signIn in value.EnumerateArray())
            {
                if (signIn.ValueKind != JsonValueKind.Object ||
                    String(signIn, "userId") is not { Length: > 0 } userId ||
                    !string.Equals(
                        String(signIn, "authenticationRequirement"),
                        SingleFactorAuthentication,
                        StringComparison.OrdinalIgnoreCase) ||
                    !Succeeded(signIn) ||
                    !DateTimeOffset.TryParse(
                        String(signIn, "createdDateTime"),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal,
                        out var created))
                {
                    continue;
                }

                totals[userId] = totals.TryGetValue(userId, out var total)
                    ? new SingleFactorSignIns(total.Count + 1, created > total.LastSignIn ? created : total.LastSignIn)
                    : new SingleFactorSignIns(1, created);
            }
        }

        return String(page, "@odata.nextLink") is { Length: > 0 } next ? next : null;
    }

    /// <summary>
    /// v1.0: <c>GET /auditLogs/signIns?$filter=createdDateTime ge {since}&amp;$top=1000</c>, following every page;
    /// v1.0 returns interactive sign-ins only. The endpoint doesn't support <c>$select</c>, so only each sign-in's
    /// <c>appliedConditionalAccessPolicies</c> (policy ID and result) is mapped and the rest dropped with the page.
    /// Progress is reported after every page and cancellation checked between pages. One request at a time; the Graph
    /// SDK's retry handler waits out throttling (429 with <c>Retry-After</c>).
    /// </summary>
    private async Task<ConditionalAccessUsage> ReadConditionalAccessUsageAsync(
        DateTimeOffset since,
        IProgress<int>? signInsRead,
        CancellationToken cancellationToken)
    {
        var aggregator = new ConditionalAccessUsageAggregator(since, _timeProvider.GetUtcNow());
        var requests = _client.AuditLogs.SignIns;

        SignInCollectionResponse? response = await requests.GetAsync(config =>
        {
            config.QueryParameters.Filter = SignInsSinceFilter(since);
            config.QueryParameters.Top = 1000;
        }, cancellationToken).ConfigureAwait(false);

        while (response is not null)
        {
            foreach (var signIn in response.Value ?? [])
            {
                aggregator.Add(MapAppliedPolicies(signIn));
            }

            signInsRead?.Report(aggregator.SignInsRead);

            if (string.IsNullOrEmpty(response.OdataNextLink))
            {
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();
            response = await requests.WithUrl(response.OdataNextLink)
                .GetAsync(_ => { }, cancellationToken).ConfigureAwait(false);
        }

        return aggregator.ToUsage();
    }

    public static string SignInsSinceFilter(DateTimeOffset since) =>
        $"createdDateTime ge {since.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}";

    /// <summary>
    /// A sign-in's applied Conditional Access policies. A missing list is empty (Graph leaves it out without
    /// Policy.Read.All), entries without an ID are dropped, and a missing or unrecognised result is unknown.
    /// </summary>
    public static IReadOnlyList<AppliedPolicyResult> MapAppliedPolicies(SignIn signIn) =>
        signIn.AppliedConditionalAccessPolicies?
            .Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Id))
            .Select(p => new AppliedPolicyResult(p.Id!, MapAppliedPolicyResult(p.Result)))
            .ToList() ?? [];

    public static ConditionalAccessResult MapAppliedPolicyResult(AppliedConditionalAccessPolicyResult? result) =>
        result switch
        {
            AppliedConditionalAccessPolicyResult.Success => ConditionalAccessResult.Success,
            AppliedConditionalAccessPolicyResult.Failure => ConditionalAccessResult.Failure,
            AppliedConditionalAccessPolicyResult.NotApplied => ConditionalAccessResult.NotApplied,
            AppliedConditionalAccessPolicyResult.NotEnabled => ConditionalAccessResult.NotEnabled,
            AppliedConditionalAccessPolicyResult.ReportOnlySuccess => ConditionalAccessResult.ReportOnlySuccess,
            AppliedConditionalAccessPolicyResult.ReportOnlyFailure => ConditionalAccessResult.ReportOnlyFailure,
            AppliedConditionalAccessPolicyResult.ReportOnlyNotApplied => ConditionalAccessResult.ReportOnlyNotApplied,
            AppliedConditionalAccessPolicyResult.ReportOnlyInterrupted => ConditionalAccessResult.ReportOnlyInterrupted,
            _ => ConditionalAccessResult.Unknown,
        };

    private static bool Succeeded(JsonElement signIn) =>
        signIn.TryGetProperty("status", out var status) &&
        status.ValueKind == JsonValueKind.Object &&
        status.TryGetProperty("errorCode", out var code) &&
        code.ValueKind == JsonValueKind.Number &&
        code.TryGetInt64(out var number) &&
        number == 0;

    private static string? String(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? Boolean(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            }
            : null;

    /// <summary>
    /// A GET to a Graph URL through the Graph client's own request adapter, returning the raw JSON, so authentication,
    /// retries and error mapping are the same as for typed requests. Used for beta, and for v1.0 values the SDK's types
    /// would drop. Graph errors come back as <see cref="ODataError"/>.
    /// </summary>
    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        var request = new RequestInformation { HttpMethod = Method.GET, URI = new Uri(url) };
        request.Headers.Add("Accept", "application/json");
        var errorMapping = new Dictionary<string, ParsableFactory<IParsable>>
        {
            ["XXX"] = ODataError.CreateFromDiscriminatorValue,
        };

        await using var stream = await _client.RequestAdapter
            .SendPrimitiveAsync<Stream>(request, errorMapping, cancellationToken).ConfigureAwait(false);

        return stream is null
            ? JsonDocument.Parse("{}")
            : await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>App role IDs to their values (permission names, for example <c>Mail.Send</c>).</summary>
    public static IReadOnlyDictionary<Guid, string> MapAppRoleNames(IEnumerable<AppRole>? appRoles)
    {
        var result = new Dictionary<Guid, string>();

        foreach (var role in appRoles ?? [])
        {
            if (role.Id is { } id && !string.IsNullOrEmpty(role.Value))
            {
                result.TryAdd(id, role.Value);
            }
        }

        return result;
    }

    /// <summary>
    /// The grant, or null when the assignment isn't to a service principal. An app role ID that isn't in
    /// <paramref name="roleNames"/> keeps the ID as its name, so it is never mistaken for a known permission.
    /// </summary>
    public static AppPermissionGrant? MapAppRoleAssignment(
        AppRoleAssignment assignment,
        IReadOnlyDictionary<Guid, string> roleNames)
    {
        if (!string.Equals(assignment.PrincipalType, "ServicePrincipal", StringComparison.OrdinalIgnoreCase) ||
            assignment.PrincipalId is not { } principalId)
        {
            return null;
        }

        var roleId = assignment.AppRoleId ?? Guid.Empty;

        return new AppPermissionGrant
        {
            ServicePrincipalId = principalId.ToString(),
            ServicePrincipalDisplayName = assignment.PrincipalDisplayName ?? string.Empty,
            PermissionName = roleNames.TryGetValue(roleId, out var name) ? name : roleId.ToString(),
            CreatedDateTime = assignment.CreatedDateTime,
        };
    }

    /// <summary>
    /// Reads every service principal. SAML apps are picked out by the check, not the query. The List
    /// servicePrincipals docs give 100 as the maximum page size, so that is what is asked for; every page is followed.
    /// Listing doesn't return the certificate data (<c>key</c>) that the SHA-1 thumbprint comes from, so each service
    /// principal with a signing certificate is then read on its own with <c>$select=keyCredentials</c>; even then,
    /// only the <c>Verify</c> credential of each pair carries <c>key</c>.
    /// </summary>
    private async Task<IReadOnlyList<EnterpriseApp>> ReadEnterpriseAppsAsync(CancellationToken cancellationToken)
    {
        var servicePrincipals = new List<ServicePrincipal>();

        ServicePrincipalCollectionResponse? response = await _client.ServicePrincipals.GetAsync(config =>
        {
            config.QueryParameters.Select =
            [
                "id", "appId", "displayName", "accountEnabled", "appOwnerOrganizationId", "preferredSingleSignOnMode",
                "preferredTokenSigningKeyThumbprint", "keyCredentials",
            ];
            config.QueryParameters.Top = 100;
        }, cancellationToken).ConfigureAwait(false);

        while (response is not null)
        {
            if (response.Value is not null)
            {
                servicePrincipals.AddRange(response.Value);
            }

            response = string.IsNullOrEmpty(response.OdataNextLink)
                ? null
                : await _client.ServicePrincipals.WithUrl(response.OdataNextLink)
                    .GetAsync(_ => { }, cancellationToken).ConfigureAwait(false);
        }

        foreach (var servicePrincipal in servicePrincipals)
        {
            if (servicePrincipal.Id is { } id && servicePrincipal.KeyCredentials?.Any(IsSigningCertificate) == true)
            {
                servicePrincipal.KeyCredentials = await ReadKeyCredentialsAsync(id, cancellationToken)
                    .ConfigureAwait(false) ?? servicePrincipal.KeyCredentials;
            }
        }

        return servicePrincipals.Select(MapServicePrincipal).ToList();
    }

    /// <summary>
    /// <c>GET /servicePrincipals/{id}?$select=keyCredentials</c>, which includes each certificate's <c>key</c>.
    /// Returns null if the service principal was deleted since it was listed.
    /// </summary>
    private async Task<List<KeyCredential>?> ReadKeyCredentialsAsync(string id, CancellationToken cancellationToken)
    {
        try
        {
            var servicePrincipal = await _client.ServicePrincipals[id].GetAsync(config =>
            {
                config.QueryParameters.Select = ["keyCredentials"];
            }, cancellationToken).ConfigureAwait(false);

            return servicePrincipal?.KeyCredentials;
        }
        catch (ApiException ex) when (ex.ResponseStatusCode == 404)
        {
            return null;
        }
    }

    public static EnterpriseApp MapServicePrincipal(ServicePrincipal servicePrincipal)
    {
        var credentials = servicePrincipal.KeyCredentials ?? [];

        return new EnterpriseApp
        {
            Id = servicePrincipal.Id ?? string.Empty,
            AppId = Guid.TryParse(servicePrincipal.AppId, out var appId) ? appId : Guid.Empty,
            DisplayName = servicePrincipal.DisplayName ?? string.Empty,
            // Graph always returns accountEnabled; if it's missing, assume enabled so findings keep their severity.
            AccountEnabled = servicePrincipal.AccountEnabled ?? true,
            AppOwnerOrganizationId = servicePrincipal.AppOwnerOrganizationId,
            PreferredSingleSignOnMode = servicePrincipal.PreferredSingleSignOnMode,
            PreferredTokenSigningKeyThumbprint = servicePrincipal.PreferredTokenSigningKeyThumbprint,
            SigningCertificates = credentials
                .Where(IsSigningCertificate)
                .Select(c => MapSigningCertificate(c, credentials))
                .ToList(),
        };
    }

    /// <summary>
    /// A token signing certificate is an <c>AsymmetricX509Cert</c> key credential with usage <c>Sign</c>. Entra
    /// stores a matching <c>Verify</c> credential for each one, which is not a separate certificate.
    /// </summary>
    public static bool IsSigningCertificate(KeyCredential credential) =>
        string.Equals(credential.Type, "AsymmetricX509Cert", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(credential.Usage, "Sign", StringComparison.OrdinalIgnoreCase);

    /// <param name="allCredentials">
    /// Every key credential of the service principal. Graph returns the certificate (<c>key</c>) only on the
    /// <c>Verify</c> credential of each pair, so the thumbprint of a <c>Sign</c> credential comes from the credential
    /// that shares its <c>customKeyIdentifier</c>.
    /// </param>
    public static SigningCertificate MapSigningCertificate(
        KeyCredential credential,
        IReadOnlyList<KeyCredential>? allCredentials = null) =>
        new()
        {
            KeyId = credential.KeyId ?? Guid.Empty,
            Thumbprint = Thumbprint(PairedCredentialWithKey(credential, allCredentials) ?? credential),
            DisplayName = credential.DisplayName ?? string.Empty,
            StartDateTime = credential.StartDateTime ?? default,
            EndDateTime = credential.EndDateTime,
        };

    private static KeyCredential? PairedCredentialWithKey(
        KeyCredential credential,
        IReadOnlyList<KeyCredential>? allCredentials)
    {
        if (credential.Key is { Length: > 0 } || credential.CustomKeyIdentifier is not { Length: > 0 } identifier)
        {
            return null;
        }

        return allCredentials?.FirstOrDefault(c =>
            c != credential &&
            c.Key is { Length: > 0 } &&
            c.CustomKeyIdentifier is { } other &&
            other.AsSpan().SequenceEqual(identifier));
    }

    /// <summary>
    /// The certificate's SHA-1 thumbprint in uppercase hex, the form <c>preferredTokenSigningKeyThumbprint</c> uses: the
    /// SHA-1 hash of <c>key</c> (the certificate's DER bytes). Without <c>key</c>, <c>customKeyIdentifier</c> is used
    /// only when it is SHA-1 sized; Entra sets it to a SHA-256 hash for SAML signing certificates, which never matches.
    /// </summary>
    public static string Thumbprint(KeyCredential credential)
    {
        if (credential.Key is { Length: > 0 } key)
        {
            // An identifier, not a security use of SHA-1: it is how Entra names the active certificate.
            return Convert.ToHexString(SHA1.HashData(key));
        }

        return credential.CustomKeyIdentifier is { Length: 20 } identifier
            ? Convert.ToHexString(identifier)
            : string.Empty;
    }

    /// <summary>
    /// <c>GET /reports/authenticationMethods/userRegistrationDetails?$filter=isMfaCapable eq false</c>, following every
    /// page. Only users who aren't MFA capable are asked for; the check still tests the flag. The report covers cloud
    /// and synced users alike and needs an Entra ID P1 or P2 licence. The docs list no <c>$top</c> for it, so Graph's
    /// default page size is used.
    /// </summary>
    private async Task<IReadOnlyList<UserRegistration>> ReadUserRegistrationsAsync(CancellationToken cancellationToken)
    {
        var result = new List<UserRegistration>();
        var requests = _client.Reports.AuthenticationMethods.UserRegistrationDetails;

        UserRegistrationDetailsCollectionResponse? response = await requests.GetAsync(config =>
        {
            config.QueryParameters.Filter = "isMfaCapable eq false";
        }, cancellationToken).ConfigureAwait(false);

        while (response is not null)
        {
            if (response.Value is not null)
            {
                result.AddRange(response.Value.Select(MapUserRegistration));
            }

            response = string.IsNullOrEmpty(response.OdataNextLink)
                ? null
                : await requests.WithUrl(response.OdataNextLink)
                    .GetAsync(_ => { }, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    public static UserRegistration MapUserRegistration(UserRegistrationDetails details) =>
        new()
        {
            Id = details.Id ?? string.Empty,
            UserPrincipalName = details.UserPrincipalName ?? string.Empty,
            DisplayName = details.UserDisplayName ?? string.Empty,
            // Anything but guest, including a missing or future value, counts as a member so it isn't played down.
            UserType = details.UserType == SignInUserType.Guest ? UserType.Guest : UserType.Member,
            IsAdmin = details.IsAdmin == true,
            IsMfaRegistered = details.IsMfaRegistered == true,
            // Missing means not capable, so the user is reported rather than silently skipped.
            IsMfaCapable = details.IsMfaCapable == true,
            MethodsRegistered = details.MethodsRegistered?.Where(m => !string.IsNullOrEmpty(m)).ToList() ?? [],
            LastUpdatedDateTime = details.LastUpdatedDateTime,
        };

    /// <summary>
    /// Reads every group, then which groups have at least one direct member and how many owners. Both come from two
    /// more group listings that expand <c>members</c> and <c>owners</c>: an expanded relationship of a directory
    /// object returns at most 20 items and has no next link, which is enough to tell empty from not empty and one
    /// owner from several. The relationships are expanded in separate listings because Graph doesn't promise more
    /// than one expand per request. Never uses <c>POST /$batch</c>.
    /// </summary>
    private async Task<IReadOnlyList<DirectoryGroup>> ReadGroupsAsync(CancellationToken cancellationToken)
    {
        var groups = await ListGroupsAsync(config =>
        {
            config.QueryParameters.Select =
            [
                "id", "displayName", "groupTypes", "securityEnabled", "mailEnabled", "onPremisesSyncEnabled",
                "isAssignableToRole", "createdDateTime", "membershipRuleProcessingState", "visibility",
                "onPremisesProvisioningErrors",
            ];
            config.QueryParameters.Top = 999;
        }, cancellationToken).ConfigureAwait(false);

        var memberCounts = await ListRelatedCountsAsync("members", g => g.Members, cancellationToken)
            .ConfigureAwait(false);
        var ownerCounts = await ListRelatedCountsAsync("owners", g => g.Owners, cancellationToken)
            .ConfigureAwait(false);

        return groups
            .Select(g => MapGroup(
                g,
                memberCounts.ContainsKey(g.Id ?? string.Empty),
                ownerCounts.GetValueOrDefault(g.Id ?? string.Empty)))
            .ToList();
    }

    /// <summary>
    /// <c>GET /groups?$filter=hasMembersWithLicenseErrors eq true&amp;$select=id</c>, following every page. The List
    /// groups docs show this filter without <c>ConsistencyLevel</c> or <c>$count</c>; the property is never returned,
    /// so it can only be filtered on.
    /// </summary>
    private async Task<IReadOnlyList<string>> ReadGroupIdsWithLicenseErrorsAsync(CancellationToken cancellationToken)
    {
        var groups = await ListGroupsAsync(config =>
        {
            config.QueryParameters.Filter = "hasMembersWithLicenseErrors eq true";
            config.QueryParameters.Select = ["id"];
        }, cancellationToken).ConfigureAwait(false);

        return groups.Where(g => !string.IsNullOrEmpty(g.Id)).Select(g => g.Id!).ToList();
    }

    /// <summary>
    /// How many related objects each group has, by group ID, at most 20 (the expand limit). Groups with none aren't in
    /// the result.
    /// </summary>
    private async Task<Dictionary<string, int>> ListRelatedCountsAsync(
        string relationship,
        Func<Group, List<DirectoryObject>?> related,
        CancellationToken cancellationToken)
    {
        var groups = await ListGroupsAsync(config =>
        {
            config.QueryParameters.Select = ["id"];
            config.QueryParameters.Expand = [$"{relationship}($select=id)"];
            // Graph may return smaller pages when expanding; every page is followed either way.
            config.QueryParameters.Top = 100;
        }, cancellationToken).ConfigureAwait(false);

        return groups
            .Where(g => g.Id is not null && related(g) is { Count: > 0 })
            .ToDictionary(g => g.Id!, g => related(g)!.Count, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<List<Group>> ListGroupsAsync(
        Action<RequestConfiguration<Microsoft.Graph.Groups.GroupsRequestBuilder.GroupsRequestBuilderGetQueryParameters>> configure,
        CancellationToken cancellationToken)
    {
        var result = new List<Group>();

        GroupCollectionResponse? response =
            await _client.Groups.GetAsync(configure, cancellationToken).ConfigureAwait(false);

        while (response is not null)
        {
            if (response.Value is not null)
            {
                result.AddRange(response.Value);
            }

            response = string.IsNullOrEmpty(response.OdataNextLink)
                ? null
                : await _client.Groups.WithUrl(response.OdataNextLink)
                    .GetAsync(_ => { }, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    /// <param name="ownerCount">How many owners the owners listing returned for the group, at most 20.</param>
    public static DirectoryGroup MapGroup(Group group, bool hasMembers, int ownerCount)
    {
        var kind = MapGroupKind(group.GroupTypes, group.SecurityEnabled, group.MailEnabled);
        var isSynced = group.OnPremisesSyncEnabled == true;
        var isDynamic = HasGroupType(group.GroupTypes, "DynamicMembership");

        return new DirectoryGroup
        {
            Id = group.Id ?? string.Empty,
            DisplayName = group.DisplayName ?? string.Empty,
            Kind = kind,
            IsDynamic = isDynamic,
            IsSyncedFromOnPremises = isSynced,
            IsRoleAssignable = group.IsAssignableToRole == true,
            CreatedDateTime = group.CreatedDateTime,
            HasMembers = hasMembers,
            // Owners Graph does return are still counted for the other kinds, so groups with cloud owners are
            // recognised; an empty list there is unknown.
            OwnerCount = OwnersAvailable(kind, isSynced) || ownerCount > 0 ? ownerCount : null,
            Visibility = MapGroupVisibility(group.Visibility),
            IsDynamicProcessingPaused = IsProcessingPaused(isDynamic, group.MembershipRuleProcessingState),
            SyncErrors = MapGroupSyncErrors(group.OnPremisesProvisioningErrors),
        };
    }

    /// <summary><c>Public</c>, <c>Private</c> or <c>HiddenMembership</c>, ignoring case; null for anything else.</summary>
    public static GroupVisibility? MapGroupVisibility(string? visibility) =>
        visibility?.ToUpperInvariant() switch
        {
            "PUBLIC" => GroupVisibility.Public,
            "PRIVATE" => GroupVisibility.Private,
            "HIDDENMEMBERSHIP" => GroupVisibility.HiddenMembership,
            _ => null,
        };

    /// <summary>
    /// True when a dynamic group's <c>membershipRuleProcessingState</c> is <c>Paused</c>, ignoring case. Any other or
    /// missing value, and any group that isn't dynamic, is not paused.
    /// </summary>
    public static bool IsProcessingPaused(bool isDynamic, string? membershipRuleProcessingState) =>
        isDynamic && string.Equals(membershipRuleProcessingState, "Paused", StringComparison.OrdinalIgnoreCase);

    /// <summary>The group's <c>onPremisesProvisioningErrors</c>, with missing text as empty strings.</summary>
    public static IReadOnlyList<GroupSyncError> MapGroupSyncErrors(IEnumerable<OnPremisesProvisioningError?>? errors) =>
        errors?
            .OfType<OnPremisesProvisioningError>()
            .Select(e => new GroupSyncError
            {
                Category = e.Category ?? string.Empty,
                PropertyCausingError = e.PropertyCausingError ?? string.Empty,
                Value = e.Value ?? string.Empty,
                OccurredDateTime = e.OccurredDateTime,
            })
            .ToList() ?? [];

    /// <summary>
    /// <c>Unified</c> in groupTypes is a Microsoft 365 group; otherwise security and mail enabled is a mail-enabled
    /// security group, security only a security group, and mail only a distribution group.
    /// </summary>
    public static GroupKind MapGroupKind(IEnumerable<string>? groupTypes, bool? securityEnabled, bool? mailEnabled)
    {
        if (HasGroupType(groupTypes, "Unified"))
        {
            return GroupKind.Microsoft365;
        }

        return (securityEnabled == true, mailEnabled == true) switch
        {
            (true, true) => GroupKind.MailEnabledSecurity,
            (false, true) => GroupKind.Distribution,
            // Security only, and the unexpected case of neither flag: treat it as a security group.
            _ => GroupKind.Security,
        };
    }

    /// <summary>
    /// Whether an empty owner list means the group has no owners in Entra ID. True for Microsoft 365 and security
    /// groups, including synced security groups: their owner may still be set in Active Directory (managedBy),
    /// which the no-owners check says. Graph documents owners as unavailable for distribution groups,
    /// mail-enabled security groups and other synced groups, so an empty list for them means unknown. Groups
    /// converted to cloud management (source of authority) have <c>onPremisesSyncEnabled</c> null and count as
    /// cloud groups.
    /// </summary>
    public static bool OwnersAvailable(GroupKind kind, bool isSyncedFromOnPremises) =>
        kind == GroupKind.Security || (kind == GroupKind.Microsoft365 && !isSyncedFromOnPremises);

    private static bool HasGroupType(IEnumerable<string>? groupTypes, string groupType) =>
        groupTypes?.Any(t => string.Equals(t, groupType, StringComparison.OrdinalIgnoreCase)) == true;

    /// <param name="hasOwners">Whether the owners listing returned at least one owner for the application.</param>
    public static AppRegistration MapApplication(Application application, bool hasOwners = true)
    {
        var credentials = new List<AppCredential>();

        if (application.PasswordCredentials is not null)
        {
            foreach (var credential in application.PasswordCredentials)
            {
                credentials.Add(MapCredential(credential.KeyId, AppCredentialType.Secret, credential.DisplayName, credential.StartDateTime, credential.EndDateTime));
            }
        }

        if (application.KeyCredentials is not null)
        {
            foreach (var credential in application.KeyCredentials)
            {
                credentials.Add(MapCredential(credential.KeyId, AppCredentialType.Certificate, credential.DisplayName, credential.StartDateTime, credential.EndDateTime));
            }
        }

        return new AppRegistration
        {
            Id = application.Id ?? string.Empty,
            AppId = Guid.TryParse(application.AppId, out var appId) ? appId : Guid.Empty,
            DisplayName = application.DisplayName ?? string.Empty,
            Credentials = credentials,
            SignInAudience = application.SignInAudience,
            IdentifierUris = application.IdentifierUris?.Where(u => !string.IsNullOrEmpty(u)).ToList() ?? [],
            RedirectUris = MapRedirectUris(application),
            HasOwners = hasOwners,
        };
    }

    /// <summary>The redirect URIs of the Web, single-page application and public client platforms, in that order.</summary>
    public static IReadOnlyList<RedirectUri> MapRedirectUris(Application application)
    {
        var result = new List<RedirectUri>();
        Add(RedirectUriPlatform.Web, application.Web?.RedirectUris);
        Add(RedirectUriPlatform.Spa, application.Spa?.RedirectUris);
        Add(RedirectUriPlatform.PublicClient, application.PublicClient?.RedirectUris);
        return result;

        void Add(RedirectUriPlatform platform, IEnumerable<string>? uris)
        {
            foreach (var uri in uris ?? [])
            {
                if (uri is not null)
                {
                    result.Add(new RedirectUri(platform, uri));
                }
            }
        }
    }

    private static AppCredential MapCredential(
        Guid? keyId,
        AppCredentialType type,
        string? displayName,
        DateTimeOffset? start,
        DateTimeOffset? end) =>
        new()
        {
            KeyId = keyId ?? Guid.Empty,
            Type = type,
            // Graph often omits credential display names; fall back to the key ID so messages stay identifiable.
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? (keyId ?? Guid.Empty).ToString() : displayName,
            StartDateTime = start ?? default,
            EndDateTime = end,
        };
}
