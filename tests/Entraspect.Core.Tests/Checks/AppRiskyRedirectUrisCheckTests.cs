using Entraspect.Core.Checks;

namespace Entraspect.Core.Tests.Checks;

public sealed class AppRiskyRedirectUrisCheckTests
{
    private const string Wildcard = "Wildcard redirect URI.";
    private const string Http = "Redirect URI uses unencrypted HTTP.";
    private const string Loopback = "Redirect URI points to this computer; usually left over from development.";
    private const string Invalid = "Redirect URI is not a valid URI.";

    private static AppRegistration App(string id, params RedirectUri[] redirectUris) =>
        new()
        {
            Id = id,
            AppId = Guid.NewGuid(),
            DisplayName = $"App {id}",
            Credentials = [],
            RedirectUris = redirectUris,
        };

    private static Task<IReadOnlyList<Finding>> RunAsync(params AppRegistration[] apps) =>
        new AppRiskyRedirectUrisCheck().RunAsync(new FakeDirectoryReader(apps), new CheckOptions());

    private static async Task<Finding?> RunOneAsync(RedirectUriPlatform platform, string uri) =>
        (await RunAsync(App("1", new RedirectUri(platform, uri)))).SingleOrDefault();

    [Theory]
    [InlineData(RedirectUriPlatform.Web, "https://contoso.com/signin-oidc")]
    [InlineData(RedirectUriPlatform.Spa, "https://app.contoso.com/")]
    [InlineData(RedirectUriPlatform.PublicClient, "https://login.microsoftonline.com/common/oauth2/nativeclient")]
    [InlineData(RedirectUriPlatform.PublicClient, "msal11111111-1111-1111-1111-111111111111://auth")]
    [InlineData(RedirectUriPlatform.PublicClient, "urn:ietf:wg:oauth:2.0:oob")]
    [InlineData(RedirectUriPlatform.PublicClient, "ms-appx-web://microsoft.aad.brokerplugin/11111111-1111-1111-1111-111111111111")]
    public async Task Https_and_custom_scheme_uris_have_no_finding(RedirectUriPlatform platform, string uri)
    {
        Assert.Null(await RunOneAsync(platform, uri));
    }

    [Theory]
    [InlineData(RedirectUriPlatform.Web, "https://*.contoso.com/signin")]
    [InlineData(RedirectUriPlatform.Spa, "https://contoso.com/*")]
    [InlineData(RedirectUriPlatform.PublicClient, "https://*.contoso.com")]
    [InlineData(RedirectUriPlatform.Web, "http://*.contoso.com")]
    public async Task Wildcard_is_a_warning_on_every_platform(RedirectUriPlatform platform, string uri)
    {
        var f = await RunOneAsync(platform, uri);

        Assert.NotNull(f);
        Assert.Equal(Severity.Warning, f.Severity);
        Assert.Equal(Wildcard, f.Message);
    }

    [Theory]
    [InlineData(RedirectUriPlatform.Web, "http://example.com")]
    [InlineData(RedirectUriPlatform.Web, "HTTP://example.com/callback")]
    [InlineData(RedirectUriPlatform.Spa, "http://app.contoso.com/")]
    [InlineData(RedirectUriPlatform.Web, "http://127.0.0.2/")]
    public async Task Http_to_another_host_on_web_or_spa_is_a_warning(RedirectUriPlatform platform, string uri)
    {
        var f = await RunOneAsync(platform, uri);

        Assert.NotNull(f);
        Assert.Equal(Severity.Warning, f.Severity);
        Assert.Equal(Http, f.Message);
    }

    [Fact]
    public async Task Http_to_another_host_on_public_client_has_no_finding()
    {
        Assert.Null(await RunOneAsync(RedirectUriPlatform.PublicClient, "http://example.com"));
    }

    [Theory]
    [InlineData(RedirectUriPlatform.Web, "http://localhost")]
    [InlineData(RedirectUriPlatform.Web, "https://localhost:5001/signin-oidc")]
    [InlineData(RedirectUriPlatform.Web, "http://LOCALHOST:8080")]
    [InlineData(RedirectUriPlatform.Spa, "http://127.0.0.1:3000")]
    [InlineData(RedirectUriPlatform.Spa, "http://[::1]:3000/")]
    public async Task Loopback_on_web_or_spa_is_info(RedirectUriPlatform platform, string uri)
    {
        var f = await RunOneAsync(platform, uri);

        Assert.NotNull(f);
        Assert.Equal(Severity.Info, f.Severity);
        Assert.Equal(Loopback, f.Message);
    }

    [Theory]
    [InlineData("http://localhost")]
    [InlineData("http://localhost:8400")]
    [InlineData("http://127.0.0.1")]
    [InlineData("http://[::1]")]
    public async Task Loopback_on_public_client_has_no_finding(string uri)
    {
        Assert.Null(await RunOneAsync(RedirectUriPlatform.PublicClient, uri));
    }

    [Theory]
    [InlineData(RedirectUriPlatform.Web, "not a uri")]
    [InlineData(RedirectUriPlatform.Web, "/signin-oidc")]
    [InlineData(RedirectUriPlatform.Spa, "contoso.com/callback")]
    [InlineData(RedirectUriPlatform.PublicClient, "")]
    [InlineData(RedirectUriPlatform.Web, "https://")]
    public async Task Uri_that_is_not_a_valid_absolute_uri_is_info(RedirectUriPlatform platform, string uri)
    {
        var f = await RunOneAsync(platform, uri);

        Assert.NotNull(f);
        Assert.Equal(Severity.Info, f.Severity);
        Assert.Equal(Invalid, f.Message);
    }

    [Theory]
    [InlineData(RedirectUriPlatform.Web, "Web")]
    [InlineData(RedirectUriPlatform.Spa, "Single-page application")]
    public async Task Finding_names_the_platform_and_uri_in_their_own_fields(
        RedirectUriPlatform platform,
        string platformName)
    {
        var f = await RunOneAsync(platform, "http://example.com");

        Assert.NotNull(f);
        Assert.Equal("app-risky-redirect-uris", f.CheckId);
        Assert.Equal("AppRegistration", f.ObjectType);
        Assert.Equal("1", f.ObjectId);
        Assert.Equal("App 1", f.ObjectDisplayName);
        Assert.Equal(platformName, f.SubObjectType);
        Assert.Equal("http://example.com", f.SubObjectDisplayName);
        Assert.Null(f.DueDate);
    }

    [Fact]
    public async Task Each_matching_uri_gives_one_finding()
    {
        var findings = await RunAsync(
            App(
                "1",
                new RedirectUri(RedirectUriPlatform.Web, "http://example.com"),
                new RedirectUri(RedirectUriPlatform.Web, "https://contoso.com"),
                new RedirectUri(RedirectUriPlatform.Spa, "http://localhost:3000"),
                new RedirectUri(RedirectUriPlatform.PublicClient, "http://localhost")),
            App("2", new RedirectUri(RedirectUriPlatform.Web, "https://*.contoso.com")));

        Assert.Equal(
            new[] { ("1", Http), ("1", Loopback), ("2", Wildcard) },
            findings.Select(f => (f.ObjectId, f.Message)));
    }

    [Fact]
    public void Platform_names_match_the_authentication_page()
    {
        Assert.Equal(
            "Mobile and desktop applications",
            AppRiskyRedirectUrisCheck.PlatformName(RedirectUriPlatform.PublicClient));
    }
}
