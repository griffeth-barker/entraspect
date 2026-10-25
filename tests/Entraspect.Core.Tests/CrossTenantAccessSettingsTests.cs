namespace Entraspect.Core.Tests;

public sealed class CrossTenantAccessSettingsTests
{
    private static readonly CrossTenantAccessTarget AllowedAll = new(CrossTenantAccessType.Allowed, IncludesAll: true);
    private static readonly CrossTenantAccessTarget AllowedSome = new(CrossTenantAccessType.Allowed, IncludesAll: false);
    private static readonly CrossTenantAccessTarget BlockedAll = new(CrossTenantAccessType.Blocked, IncludesAll: true);
    private static readonly CrossTenantAccessTarget BlockedSome = new(CrossTenantAccessType.Blocked, IncludesAll: false);
    private static readonly CrossTenantAccessTarget Unknown = new(CrossTenantAccessType.Unknown, IncludesAll: true);

    public static TheoryData<CrossTenantAccessTarget?, CrossTenantAccessTarget?, bool, bool> Cases => new()
    {
        // users, applications, open to all, allowed for any
        { AllowedAll, AllowedAll, true, true },
        { AllowedAll, AllowedSome, false, true },
        { AllowedSome, AllowedAll, false, true },
        { AllowedSome, AllowedSome, false, true },
        { BlockedSome, AllowedAll, false, true },
        { AllowedAll, BlockedSome, false, true },
        { BlockedAll, BlockedAll, false, false },
        { BlockedAll, AllowedAll, false, false },
        { AllowedAll, BlockedAll, false, false },
        { Unknown, AllowedAll, false, false },
        { AllowedAll, Unknown, false, false },
        { null, AllowedAll, false, false },
        { AllowedAll, null, false, false },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Open_to_all_needs_both_sides_allowed_for_all_and_allowed_for_any_needs_neither_side_blocked_for_all(
        CrossTenantAccessTarget? users,
        CrossTenantAccessTarget? applications,
        bool openToAll,
        bool allowedForAny)
    {
        var access = new CrossTenantAccess { Users = users, Applications = applications };

        Assert.Equal(openToAll, access.IsOpenToAll);
        Assert.Equal(allowedForAny, access.IsAllowedForAny);
    }

    [Fact]
    public void New_settings_are_all_unset()
    {
        var settings = new CrossTenantAccessSettings();

        Assert.Null(settings.TenantId);
        Assert.Null(settings.InboundTrust.IsMfaAccepted);
        Assert.Null(settings.AutomaticRedemptionInbound);
        Assert.Null(settings.B2bDirectConnectInbound);
    }
}
