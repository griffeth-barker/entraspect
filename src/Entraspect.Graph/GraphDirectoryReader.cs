using System.Security.Cryptography;
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

    public Task<IReadOnlyList<EnterpriseApp>> GetEnterpriseAppsAsync(CancellationToken cancellationToken = default) =>
        ReadAsync(() => ReadEnterpriseAppsAsync(cancellationToken));

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
                "id", "appId", "displayName", "accountEnabled", "preferredSingleSignOnMode",
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
            // Owners Graph does return are still counted for the other kinds, so groups with cloud owners are
            // recognised; an empty list there is unknown.
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
