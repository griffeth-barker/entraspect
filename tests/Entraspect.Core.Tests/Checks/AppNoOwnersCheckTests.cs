using Entraspect.Core.Checks;

namespace Entraspect.Core.Tests.Checks;

public sealed class AppNoOwnersCheckTests
{
    private static AppRegistration App(string id, bool hasOwners) =>
        new()
        {
            Id = id,
            AppId = Guid.NewGuid(),
            DisplayName = $"App {id}",
            Credentials = [],
            HasOwners = hasOwners,
        };

    private static Task<IReadOnlyList<Finding>> RunAsync(params AppRegistration[] apps) =>
        new AppNoOwnersCheck().RunAsync(new FakeDirectoryReader(apps), new CheckOptions());

    [Fact]
    public async Task App_with_owners_has_no_finding()
    {
        Assert.Empty(await RunAsync(App("1", hasOwners: true)));
    }

    [Fact]
    public async Task App_built_without_owner_data_counts_as_owned()
    {
        var app = new AppRegistration { Id = "1", AppId = Guid.NewGuid(), DisplayName = "App 1", Credentials = [] };

        Assert.Empty(await RunAsync(app));
    }

    [Fact]
    public async Task Ownerless_app_is_a_warning_about_the_app_registration()
    {
        var f = Assert.Single(await RunAsync(App("1", hasOwners: false), App("2", hasOwners: true)));

        Assert.Equal("app-no-owners", f.CheckId);
        Assert.Equal(Severity.Warning, f.Severity);
        Assert.Equal("AppRegistration", f.ObjectType);
        Assert.Equal("1", f.ObjectId);
        Assert.Equal("App 1", f.ObjectDisplayName);
        Assert.Equal(
            "App registration has no owners. No one is accountable for its credentials and permissions.",
            f.Message);
        Assert.Null(f.DueDate);
        Assert.Null(f.SubObjectType);
        Assert.Null(f.SubObjectDisplayName);
    }
}
