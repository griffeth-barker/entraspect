using Entraspect.Core.Checks;
using Microsoft.Extensions.Time.Testing;
using static Entraspect.Core.Tests.Checks.GroupTestData;

namespace Entraspect.Core.Tests.Checks;

public sealed class GroupLicenseErrorsCheckTests
{
    private static Task<IReadOnlyList<Finding>> RunAsync(IEnumerable<string> idsWithErrors, params DirectoryGroup[] groups) =>
        new GroupLicenseErrorsCheck().RunAsync(
            new FakeDirectoryReader(groups) { GroupIdsWithLicenseErrors = idsWithErrors.ToList() },
            new CheckOptions());

    [Fact]
    public async Task Group_with_licence_errors_is_a_warning_that_names_its_kind()
    {
        var f = Assert.Single(await RunAsync(["1"], Group("1", GroupKind.Security), Group("2", GroupKind.Security)));

        Assert.Equal("group-license-errors", f.CheckId);
        Assert.Equal(Severity.Warning, f.Severity);
        Assert.Equal("Group", f.ObjectType);
        Assert.Equal("1", f.ObjectId);
        Assert.Equal("Group 1", f.ObjectDisplayName);
        Assert.Equal("Some members of this security group didn't get the licences it assigns.", f.Message);
    }

    [Theory]
    [InlineData(GroupKind.Microsoft365, "Some members of this Microsoft 365 group didn't get the licences it assigns.")]
    [InlineData(GroupKind.MailEnabledSecurity, "Some members of this mail-enabled security group didn't get the licences it assigns.")]
    public async Task Message_names_each_kind(GroupKind kind, string expected)
    {
        var f = Assert.Single(await RunAsync(["1"], Group("1", kind)));

        Assert.Equal(expected, f.Message);
    }

    [Fact]
    public async Task Synced_group_with_licence_errors_is_reported()
    {
        Assert.Single(await RunAsync(["1"], Group("1", isSynced: true)));
    }

    [Fact]
    public async Task Ids_match_whatever_their_case()
    {
        Assert.Single(await RunAsync(["ABC"], Group("abc")));
    }

    [Fact]
    public async Task No_groups_with_errors_has_no_finding()
    {
        Assert.Empty(await RunAsync([], Group("1")));
    }

    [Fact]
    public async Task Id_not_in_the_group_list_is_left_out()
    {
        Assert.Empty(await RunAsync(["gone"], Group("1")));
    }

    [Fact]
    public void Needs_an_entra_id_p1_licence()
    {
        Assert.Equal(LicenceRequirement.EntraIdP1, new GroupLicenseErrorsCheck().RequiredLicence);
    }

    [Fact]
    public async Task Failed_read_fails_only_this_check()
    {
        var reader = new FakeDirectoryReader([Group("1", GroupKind.Microsoft365, visibility: GroupVisibility.Public)])
        {
            GroupIdsWithLicenseErrors = ["1"],
            LicenseErrorsFailure = new DirectoryReadException("boom", 400, errorCode: "Request_UnsupportedQuery"),
        };
        var time = new FakeTimeProvider(Now);
        var checks = new ICheck[] { new GroupLicenseErrorsCheck(), new GroupPublicCheck() };
        var runner = new ScanRunner(reader, checks, ["GroupMember.Read.All"], new CheckOptions(), time);

        var results = await runner.RunAsync();

        Assert.Equal(CheckResultStatus.Failed, results[0].Status);
        Assert.Equal(400, results[0].StatusCode);
        Assert.Equal("Request_UnsupportedQuery", results[0].ErrorCode);
        Assert.Equal(CheckResultStatus.Succeeded, results[1].Status);
        Assert.Single(results[1].Findings);
    }
}
