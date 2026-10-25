using Microsoft.Extensions.Time.Testing;

namespace Entraspect.Core.Tests.Export;

/// <summary>A fixed report with fake IDs: one check with findings, one failed and one missing a permission.</summary>
internal static class ExportTestData
{
    public const string TenantId = "00000000-0000-0000-0000-0000000000t1";

    public static readonly FakeTimeProvider Time = new(new DateTimeOffset(2026, 1, 15, 12, 30, 0, TimeSpan.Zero));

    public static readonly ICheck AppCheck = new FakeCheck("app-check", "Expiring credentials", CheckCategory.AppRegistrations);

    public static readonly ICheck UserCheck = new FakeCheck("user-check", "MFA not registered", CheckCategory.Users);

    public static readonly ICheck CaCheck = new FakeCheck("ca-check", "Policy hygiene", CheckCategory.ConditionalAccess);

    public static readonly ICheck SettingsCheck =
        new FakeCheck("settings-check", "External collaboration", CheckCategory.ExternalIdentities);

    public static Finding ExpiringSecret() => new()
    {
        CheckId = AppCheck.Id,
        Severity = Severity.Critical,
        ObjectType = "Application",
        ObjectId = "00000000-0000-0000-0000-0000000000a1",
        ObjectDisplayName = "Zoë's app, \"prod\"",
        Message = "Secret expires in 3 days.",
        DueDate = new DateTimeOffset(2026, 1, 18, 9, 0, 0, TimeSpan.FromHours(2)),
        SubObjectType = "Secret",
        SubObjectDisplayName = "CI key",
    };

    public static Finding UserWithoutMfa() => new()
    {
        CheckId = UserCheck.Id,
        Severity = Severity.Warning,
        ObjectType = "User",
        ObjectId = "00000000-0000-0000-0000-0000000000u1",
        ObjectDisplayName = "=HYPERLINK(\"http://example.invalid\")",
        UserPrincipalName = "user1@example.invalid",
        Message = "No MFA method registered.",
    };

    public static Finding GuestInviteSetting() => new()
    {
        CheckId = SettingsCheck.Id,
        Severity = Severity.Warning,
        ObjectType = "AuthorizationPolicy",
        ObjectId = "allowInvitesFrom",
        ObjectDisplayName = "Guest invite restrictions",
        Message = "Anyone in the organisation, guests included, can invite guests.",
        Value = "Everyone",
    };

    /// <summary>A report with one check about tenant settings, whose finding has a current value.</summary>
    public static ScanReport SettingsReport() => ScanReport.Create(
        Time.GetUtcNow(),
        "Contoso test",
        TenantId,
        "1.2.3",
        [SettingsCheck],
        [
            new CheckResult
            {
                CheckId = SettingsCheck.Id,
                Status = CheckResultStatus.Succeeded,
                Findings = [GuestInviteSetting()],
                CompletedAt = Time.GetUtcNow(),
            },
        ]);

    public static ScanReport Report() => ScanReport.Create(
        Time.GetUtcNow(),
        "Contoso test",
        TenantId,
        "1.2.3",
        [UserCheck, AppCheck, CaCheck],
        [
            new CheckResult
            {
                CheckId = AppCheck.Id,
                Status = CheckResultStatus.Succeeded,
                Findings = [ExpiringSecret()],
                CompletedAt = Time.GetUtcNow(),
            },
            new CheckResult
            {
                CheckId = UserCheck.Id,
                Status = CheckResultStatus.Succeeded,
                Findings = [UserWithoutMfa()],
                CompletedAt = Time.GetUtcNow(),
            },
            new CheckResult
            {
                CheckId = CaCheck.Id,
                Status = CheckResultStatus.Failed,
                ErrorMessage = "Microsoft Graph returned HTTP 403: Insufficient privileges.",
                StatusCode = 403,
                ErrorCode = "Authorization_RequestDenied",
                CompletedAt = Time.GetUtcNow(),
            },
        ]);
}
