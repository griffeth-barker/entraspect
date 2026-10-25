using System.IO;
using CommunityToolkit.Mvvm.Input;
using Entraspect.App.Services;
using Entraspect.App.ViewModels;
using Entraspect.Graph;

namespace Entraspect.App.Tests;

public sealed class ConnectionProfilesViewModelTests : IDisposable
{
    private const string ClientId = "00000000-0000-0000-0000-0000000000c1";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"entraspect-profiles-vm-{Guid.NewGuid()}");
    private readonly FakeUserInteraction _ui = new();

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void Starts_with_the_last_used_profile_selected()
    {
        Save(Profile("p1", thumbprint: "AB"), Profile("p2", path: "/x.pfx"));

        var vm = Create("p2");

        Assert.Equal("p2", vm.SelectedProfile?.Name);
        Assert.Equal("p2", vm.TryGetProfileToConnect(out _)?.Name);
    }

    [Fact]
    public void An_old_profile_without_a_certificate_loads_and_asks_for_one()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "profiles.json"),
            $$"""[{"name":"old","tenantId":"t","clientId":"{{ClientId}}","authMode":"Interactive"}]""");

        var vm = Create("old");

        Assert.True(Assert.Single(vm.Profiles).NeedsCertificate);
        Assert.Null(vm.TryGetProfileToConnect(out var error));
        Assert.Equal(ConnectionProfilesViewModel.AddCertificateText, error);
    }

    [Fact]
    public void Add_saves_a_valid_profile_and_selects_it()
    {
        var vm = Create();

        vm.AddCommand.Execute(null);
        vm.Name = "new";
        vm.TenantId = "contoso.onmicrosoft.com";
        vm.ClientId = ClientId;
        vm.CertificateThumbprint = "AB";
        vm.SaveCommand.Execute(null);

        Assert.False(vm.IsEditing);
        Assert.Equal("new", vm.SelectedProfile?.Name);
        Assert.Equal("new", Assert.Single(new ProfileStore(_dir).Load(out _)).Name);
    }

    [Theory]
    [InlineData("", "t", ClientId, "AB", null, "Enter a profile name.")]
    [InlineData("n", "", ClientId, "AB", null, "Enter the tenant ID.")]
    [InlineData("n", "t", "not-a-guid", "AB", null, "Enter the client ID")]
    [InlineData("n", "t", ClientId, null, null, "Enter either a .pfx path or a certificate thumbprint.")]
    [InlineData("n", "t", ClientId, "AB", "/x.pfx", "Enter either a .pfx path or a certificate thumbprint.")]
    public void Save_rejects_invalid_profiles(
        string name, string tenant, string client, string? thumbprint, string? path, string expectedError)
    {
        var vm = Create();
        vm.AddCommand.Execute(null);
        vm.Name = name;
        vm.TenantId = tenant;
        vm.ClientId = client;
        vm.CertificateThumbprint = thumbprint ?? string.Empty;
        vm.CertificatePath = path ?? string.Empty;

        vm.SaveCommand.Execute(null);

        Assert.True(vm.IsEditing);
        Assert.StartsWith(expectedError, vm.ErrorText);
        Assert.False(File.Exists(Path.Combine(_dir, "profiles.json")));
    }

    [Fact]
    public void Edit_can_rename_a_profile_but_not_onto_another_name()
    {
        Save(Profile("p1", thumbprint: "AB"), Profile("p2", thumbprint: "CD"));
        var vm = Create("p1");

        vm.EditCommand.Execute(null);
        vm.Name = "p2";
        vm.SaveCommand.Execute(null);
        Assert.Equal("A profile named 'p2' already exists.", vm.ErrorText);

        vm.Name = "renamed";
        vm.SaveCommand.Execute(null);

        Assert.False(vm.IsEditing);
        Assert.Equal(new[] { "renamed", "p2" }, new ProfileStore(_dir).Load(out _).Select(p => p.Name));
    }

    [Fact]
    public async Task Delete_asks_first_and_does_nothing_when_cancelled()
    {
        Save(Profile("p1", thumbprint: "AB"));
        var vm = Create("p1");
        _ui.ConfirmResult = false;

        await ((IAsyncRelayCommand)vm.DeleteCommand).ExecuteAsync(null);

        Assert.Equal(1, _ui.Confirmations);
        Assert.Single(vm.Profiles);
        Assert.Single(new ProfileStore(_dir).Load(out _));
    }

    [Fact]
    public async Task Delete_removes_the_profile_when_confirmed()
    {
        Save(Profile("p1", thumbprint: "AB"), Profile("p2", thumbprint: "CD"));
        var vm = Create("p1");

        await ((IAsyncRelayCommand)vm.DeleteCommand).ExecuteAsync(null);

        Assert.Equal("p2", Assert.Single(vm.Profiles).Name);
        Assert.Equal("p2", vm.SelectedProfile?.Name);
        Assert.Equal("p2", Assert.Single(new ProfileStore(_dir).Load(out _)).Name);
    }

    [Theory]
    [InlineData("/x.pfx", null, true)]
    [InlineData("/x.pfx", "AB", false)]
    [InlineData(null, "AB", false)]
    public void Only_a_pfx_without_a_thumbprint_needs_a_password(string? path, string? thumbprint, bool expected)
    {
        Assert.Equal(expected, ConnectionProfilesViewModel.NeedsPassword(Profile("p", path, thumbprint)));
    }

    [Fact]
    public async Task Open_file_opens_profiles_json_in_the_default_app()
    {
        Save(Profile("p1", thumbprint: "AB"));
        var vm = Create("p1");

        await vm.OpenFileCommand.ExecuteAsync(null);

        Assert.Equal([(ProfilesPath, true)], _ui.OpenedFiles);
        Assert.Equal(string.Empty, vm.ErrorText);
    }

    [Fact]
    public async Task Open_file_saves_a_missing_file_first()
    {
        var vm = Create();

        await vm.OpenFileCommand.ExecuteAsync(null);

        Assert.Equal([(ProfilesPath, true)], _ui.OpenedFiles);
        Assert.Empty(new ProfileStore(_dir).Load(out _));
    }

    [Fact]
    public async Task Open_file_shows_the_path_when_no_app_can_open_it()
    {
        Save(Profile("p1", thumbprint: "AB"));
        _ui.OpenFileResult = false;
        var vm = Create("p1");

        await vm.OpenFileCommand.ExecuteAsync(null);

        Assert.Equal($"No app could open '{ProfilesPath}'.", vm.ErrorText);
    }

    [Fact]
    public void Reload_shows_edited_profiles_and_keeps_the_selection()
    {
        Save(Profile("p1", thumbprint: "AB"), Profile("p2", thumbprint: "CD"));
        var vm = Create("p2");
        Save(Profile("renamed", thumbprint: "AB"), Profile("p2", thumbprint: "EF"), Profile("p3", path: "/x.pfx"));

        vm.ReloadCommand.Execute(null);

        Assert.Equal(["renamed", "p2", "p3"], vm.Profiles.Select(p => p.Name));
        Assert.Equal("p2", vm.SelectedProfile?.Name);
        Assert.Equal("EF", vm.SelectedProfile?.Profile.CertificateThumbprint);
        Assert.Equal(string.Empty, vm.ErrorText);
        Assert.Equal("Reloaded profiles.json.", vm.StatusText);
    }

    [Theory]
    [InlineData("[{ not json", "profiles.json isn't valid")]
    [InlineData("null", "profiles.json must be a list of profiles.")]
    [InlineData("[{\"name\":\"bad\",\"tenantId\":\"t\",\"clientId\":\"not-a-guid\",\"certificateThumbprint\":\"AB\"}]",
        "Profile 'bad' in profiles.json is invalid: Enter the client ID")]
    [InlineData("[{\"name\":\"both\",\"tenantId\":\"t\",\"clientId\":\"00000000-0000-0000-0000-0000000000c1\",\"certificateThumbprint\":\"AB\",\"certificatePath\":\"/x.pfx\"}]",
        "Profile 'both' in profiles.json is invalid: Enter either a .pfx path or a certificate thumbprint.")]
    [InlineData("[{\"name\":\"d\",\"tenantId\":\"t\",\"clientId\":\"00000000-0000-0000-0000-0000000000c1\",\"certificateThumbprint\":\"AB\"},{\"name\":\"d\",\"tenantId\":\"t\",\"clientId\":\"00000000-0000-0000-0000-0000000000c1\",\"certificateThumbprint\":\"CD\"}]",
        "profiles.json has more than one profile named 'd'.")]
    public void Reload_of_an_invalid_file_shows_why_and_changes_nothing(string json, string expectedError)
    {
        Save(Profile("p1", thumbprint: "AB"));
        var vm = Create("p1");
        File.WriteAllText(ProfilesPath, json);

        vm.ReloadCommand.Execute(null);

        Assert.StartsWith(expectedError, vm.ErrorText);
        Assert.Equal("p1", Assert.Single(vm.Profiles).Name);
        Assert.Equal("p1", vm.SelectedProfile?.Name);
        Assert.Equal(json, File.ReadAllText(ProfilesPath));
        Assert.Equal(["profiles.json"], Directory.GetFiles(_dir).Select(Path.GetFileName));
    }

    [Fact]
    public void Reload_of_a_missing_file_shows_why()
    {
        var vm = Create();

        vm.ReloadCommand.Execute(null);

        Assert.Equal($"'{ProfilesPath}' doesn't exist.", vm.ErrorText);
        Assert.False(File.Exists(ProfilesPath));
    }

    [Fact]
    public void Reload_is_unavailable_while_editing()
    {
        var vm = Create();

        vm.AddCommand.Execute(null);

        Assert.False(vm.ReloadCommand.CanExecute(null));
    }

    private string ProfilesPath => Path.Combine(_dir, "profiles.json");

    private ConnectionProfilesViewModel Create(string? lastUsed = null) =>
        new(new ProfileStore(_dir), _ui, new RelayCommand(() => { }), lastUsed);

    private void Save(params ConnectionProfile[] profiles) => new ProfileStore(_dir).Save(profiles);

    private static ConnectionProfile Profile(string name, string? path = null, string? thumbprint = null) => new()
    {
        Name = name, TenantId = "t", ClientId = ClientId, CertificatePath = path, CertificateThumbprint = thumbprint,
    };
}
