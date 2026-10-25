using Azure.Identity;
using Entraspect.Core;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Kiota.Abstractions;

namespace Entraspect.Graph;

public sealed class GraphDirectoryReader : IDirectoryReader
{
    private readonly GraphServiceClient _client;

    internal GraphDirectoryReader(GraphServiceClient client) => _client = client;

    public Task<IReadOnlyList<AppRegistration>> GetAppRegistrationsAsync(
        CancellationToken cancellationToken = default) =>
        ReadAsync(() => ReadAppRegistrationsAsync(cancellationToken));

    public Task<IReadOnlyList<DirectoryGroup>> GetGroupsAsync(CancellationToken cancellationToken = default) =>
        ReadAsync(() => ReadGroupsAsync(cancellationToken));

    // Turns Graph SDK and Azure.Identity failures into DirectoryReadException, so no SDK types leave this assembly.
    private static async Task<T> ReadAsync<T>(Func<Task<T>> read)
    {
        try
        {
            return await read().ConfigureAwait(false);
        }
        catch (ApiException ex)
        {
            var detail = ex is ODataError { Error.Message: { } message } ? message : ex.Message;
            throw new DirectoryReadException(
                $"Microsoft Graph returned HTTP {ex.ResponseStatusCode}: {detail}",
                ex.ResponseStatusCode,
                ex);
        }
        catch (AuthenticationFailedException ex)
        {
            throw new DirectoryReadException($"Sign-in failed: {ex.Message}", innerException: ex);
        }
    }

    private async Task<IReadOnlyList<AppRegistration>> ReadAppRegistrationsAsync(CancellationToken cancellationToken)
    {
        var result = new List<AppRegistration>();

        ApplicationCollectionResponse? response = await _client.Applications.GetAsync(config =>
        {
            config.QueryParameters.Select =
            [
                "id", "appId", "displayName", "passwordCredentials", "keyCredentials",
            ];
            config.QueryParameters.Top = 999;
        }, cancellationToken).ConfigureAwait(false);

        while (response is not null)
        {
            if (response.Value is not null)
            {
                foreach (var application in response.Value)
                {
                    result.Add(MapApplication(application));
                }
            }

            response = string.IsNullOrEmpty(response.OdataNextLink)
                ? null
                : await _client.Applications.WithUrl(response.OdataNextLink)
                    .GetAsync(_ => { }, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>
    /// Reads every group, then which groups have at least one direct member and at least one owner. Presence comes
    /// from two more group listings that expand <c>members</c> and <c>owners</c>: an expanded relationship of a
    /// directory object returns at most 20 items and has no next link, which is enough to tell empty from not empty.
    /// The relationships are expanded in separate listings because Graph doesn't promise more than one expand per
    /// request. Never uses <c>POST /$batch</c>.
    /// </summary>
    private async Task<IReadOnlyList<DirectoryGroup>> ReadGroupsAsync(CancellationToken cancellationToken)
    {
        var groups = await ListGroupsAsync(config =>
        {
            config.QueryParameters.Select =
            [
                "id", "displayName", "groupTypes", "securityEnabled", "mailEnabled", "onPremisesSyncEnabled",
                "isAssignableToRole", "createdDateTime",
            ];
            config.QueryParameters.Top = 999;
        }, cancellationToken).ConfigureAwait(false);

        var withMembers = await ListGroupIdsWithAnyAsync("members", g => g.Members, cancellationToken)
            .ConfigureAwait(false);
        var withOwners = await ListGroupIdsWithAnyAsync("owners", g => g.Owners, cancellationToken)
            .ConfigureAwait(false);

        return groups
            .Select(g => MapGroup(g, withMembers.Contains(g.Id ?? string.Empty), withOwners.Contains(g.Id ?? string.Empty)))
            .ToList();
    }

    private async Task<HashSet<string>> ListGroupIdsWithAnyAsync(
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
            .Select(g => g.Id!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
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

    public static DirectoryGroup MapGroup(Group group, bool hasMembers, bool hasOwners)
    {
        var kind = MapGroupKind(group.GroupTypes, group.SecurityEnabled, group.MailEnabled);
        var isSynced = group.OnPremisesSyncEnabled == true;

        return new DirectoryGroup
        {
            Id = group.Id ?? string.Empty,
            DisplayName = group.DisplayName ?? string.Empty,
            Kind = kind,
            IsDynamic = HasGroupType(group.GroupTypes, "DynamicMembership"),
            IsSyncedFromOnPremises = isSynced,
            IsRoleAssignable = group.IsAssignableToRole == true,
            CreatedDateTime = group.CreatedDateTime,
            HasMembers = hasMembers,
            // Owners Graph does return are still counted for kinds it documents as unavailable, so hybrid groups
            // with cloud owners are recognised; an empty list there is unknown.
            HasOwners = OwnersAvailable(kind, isSynced) || hasOwners ? hasOwners : null,
        };
    }

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
    /// Graph documents owners as unavailable for groups synced from on-premises, distribution groups and
    /// mail-enabled security groups, so an empty owner list for them means unknown, not none. Groups converted to
    /// cloud management (source of authority) have <c>onPremisesSyncEnabled</c> null and count as cloud groups.
    /// </summary>
    public static bool OwnersAvailable(GroupKind kind, bool isSyncedFromOnPremises) =>
        !isSyncedFromOnPremises && kind is GroupKind.Microsoft365 or GroupKind.Security;

    private static bool HasGroupType(IEnumerable<string>? groupTypes, string groupType) =>
        groupTypes?.Any(t => string.Equals(t, groupType, StringComparison.OrdinalIgnoreCase)) == true;

    public static AppRegistration MapApplication(Application application)
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
        };
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
