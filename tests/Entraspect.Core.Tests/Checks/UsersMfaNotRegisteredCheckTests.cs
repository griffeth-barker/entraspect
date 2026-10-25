using Entraspect.Core.Checks;

namespace Entraspect.Core.Tests.Checks;

public sealed class UsersMfaNotRegisteredCheckTests
{
    private const string GuestNote =
        " Guests may do MFA in their home tenant, depending on your cross-tenant access settings.";

    private static async Task<IReadOnlyList<Finding>> RunAsync(params UserRegistration[] users) =>
        await new UsersMfaNotRegisteredCheck().RunAsync(new FakeDirectoryReader(users), new CheckOptions());

    private static UserRegistration User(
        string id,
        UserType userType = UserType.Member,
        bool isAdmin = false,
        bool isMfaRegistered = false,
        bool isMfaCapable = false,
        string? displayName = null,
        string? userPrincipalName = null,
        DateTimeOffset? lastUpdated = null) =>
        new()
        {
            Id = id,
            DisplayName = displayName ?? $"User {id}",
            UserPrincipalName = userPrincipalName ?? $"user{id}@contoso.example",
            UserType = userType,
            IsAdmin = isAdmin,
            IsMfaRegistered = isMfaRegistered,
            IsMfaCapable = isMfaCapable,
            MethodsRegistered = isMfaRegistered ? ["sms"] : [],
            LastUpdatedDateTime = lastUpdated,
        };

    [Fact]
    public async Task Member_not_registered_is_a_warning_about_the_user()
    {
        var f = Assert.Single(await RunAsync(User("1")));

        Assert.Equal("users-mfa-not-registered", f.CheckId);
        Assert.Equal(Severity.Warning, f.Severity);
        Assert.Equal("User", f.ObjectType);
        Assert.Equal("1", f.ObjectId);
        Assert.Equal("User 1", f.ObjectDisplayName);
        Assert.Equal("user1@contoso.example", f.UserPrincipalName);
        Assert.Equal("Not registered for MFA.", f.Message);
        Assert.Null(f.DueDate);
        Assert.Null(f.SubObjectType);
        Assert.Null(f.SubObjectDisplayName);
    }

    [Fact]
    public async Task Admin_not_registered_is_critical()
    {
        var f = Assert.Single(await RunAsync(User("1", isAdmin: true)));

        Assert.Equal(Severity.Critical, f.Severity);
        Assert.Equal("Not registered for MFA.", f.Message);
    }

    [Fact]
    public async Task Guest_not_registered_is_info_with_the_home_tenant_note()
    {
        var f = Assert.Single(await RunAsync(User("1", UserType.Guest)));

        Assert.Equal(Severity.Info, f.Severity);
        Assert.Equal("Not registered for MFA." + GuestNote, f.Message);
    }

    [Fact]
    public async Task Guest_admin_is_critical_and_keeps_the_home_tenant_note()
    {
        var f = Assert.Single(await RunAsync(User("1", UserType.Guest, isAdmin: true)));

        Assert.Equal(Severity.Critical, f.Severity);
        Assert.Equal("Not registered for MFA." + GuestNote, f.Message);
    }

    [Fact]
    public async Task Registered_but_not_capable_says_the_methods_are_not_allowed()
    {
        var f = Assert.Single(await RunAsync(User("1", isMfaRegistered: true)));

        Assert.Equal(Severity.Warning, f.Severity);
        Assert.Equal("Registered MFA methods are not allowed by the current policy.", f.Message);
    }

    [Fact]
    public async Task Capable_users_have_no_finding()
    {
        Assert.Empty(await RunAsync(
            User("1", isMfaRegistered: true, isMfaCapable: true),
            User("2", UserType.Guest, isMfaRegistered: true, isMfaCapable: true),
            User("3", isAdmin: true, isMfaRegistered: true, isMfaCapable: true)));
    }

    [Fact]
    public async Task Mix_reports_only_users_who_are_not_capable()
    {
        var findings = await RunAsync(
            User("admin", isAdmin: true),
            User("member", isMfaRegistered: true),
            User("guest", UserType.Guest),
            User("capable", isMfaRegistered: true, isMfaCapable: true));

        Assert.Equal(
            new[] { ("admin", Severity.Critical), ("member", Severity.Warning), ("guest", Severity.Info) },
            findings.Select(f => (f.ObjectId, f.Severity)));
    }

    [Fact]
    public async Task Duplicate_display_names_stay_apart_by_user_principal_name()
    {
        var findings = await RunAsync(
            User("1", displayName: "Alex Wilber", userPrincipalName: "alexw@contoso.example"),
            User("2", displayName: "Alex Wilber", userPrincipalName: "alex.wilber@contoso.example"));

        Assert.All(findings, f => Assert.Equal("Alex Wilber", f.ObjectDisplayName));
        Assert.Equal(
            new[] { "alexw@contoso.example", "alex.wilber@contoso.example" },
            findings.Select(f => f.UserPrincipalName));
    }

    [Fact]
    public async Task Data_updated_is_the_most_recent_report_update()
    {
        var reader = new FakeDirectoryReader(new[]
        {
            User("1", lastUpdated: new DateTimeOffset(2026, 10, 23, 6, 0, 0, TimeSpan.Zero)),
            User("2", lastUpdated: new DateTimeOffset(2026, 10, 24, 6, 0, 0, TimeSpan.Zero)),
            User("3"),
        });

        var updated = await new UsersMfaNotRegisteredCheck().GetDataUpdatedAsync(reader);

        Assert.Equal(new DateTimeOffset(2026, 10, 24, 6, 0, 0, TimeSpan.Zero), updated);
    }

    [Fact]
    public async Task Data_updated_is_null_with_no_report_rows()
    {
        Assert.Null(await new UsersMfaNotRegisteredCheck().GetDataUpdatedAsync(
            new FakeDirectoryReader(Array.Empty<UserRegistration>())));
    }
}
