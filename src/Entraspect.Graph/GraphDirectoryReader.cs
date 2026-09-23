using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Entraspect.Core;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace Entraspect.Graph;

public sealed class GraphDirectoryReader : IDirectoryReader
{
    private readonly GraphServiceClient _client;

    public GraphDirectoryReader(GraphServiceClient client) => _client = client;

    public async Task<IReadOnlyList<AppRegistration>> GetAppRegistrationsAsync(
        CancellationToken cancellationToken = default)
    {
        var result = new List<AppRegistration>();

        ApplicationCollectionResponse? response = await _client.Applications.GetAsync(config =>
        {
            config.QueryParameters.Select = new[]
            {
                "id", "appId", "displayName", "passwordCredentials", "keyCredentials",
            };
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
            AppId = application.AppId is null ? Guid.Empty : Guid.Parse(application.AppId),
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
            DisplayName = displayName ?? string.Empty,
            StartDateTime = start ?? default,
            EndDateTime = end,
        };
}
