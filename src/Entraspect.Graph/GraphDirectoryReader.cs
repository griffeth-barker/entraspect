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

    public GraphDirectoryReader(GraphServiceClient client) => _client = client;

    public async Task<IReadOnlyList<AppRegistration>> GetAppRegistrationsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await ReadAppRegistrationsAsync(cancellationToken).ConfigureAwait(false);
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
