using System.Collections.Generic;
using Entraspect.Core.Checks;

namespace Entraspect.Core.Tests.Checks;

/// <summary>Fake users, roles and role-assignable groups for the Roles &amp; admins checks.</summary>
internal static class AdminTestData
{
    public const string PrivilegedRoleAdministrator = "e8611ab8-c189-46e8-94e1-60213ab1f814";
    public const string SecurityAdministrator = "194ae4cb-b126-40b2-bd5b-6091b380977d";
    public const string GlobalReader = "f2ef992c-3afb-46b9-b7cf-a126ee74c451";
    public const string DirectoryReaders = "88d8e3e3-8f55-4a1e-953a-9b9898b8876b";
    public const string RoleGroupId = "99999999-0000-0000-0000-000000000001";

    public static readonly DirectoryRoleDefinition[] Definitions =
    [
        new() { Id = PrivilegedRoles.GlobalAdministrator, TemplateId = PrivilegedRoles.GlobalAdministrator, DisplayName = "Global Administrator" },
        new() { Id = PrivilegedRoleAdministrator, TemplateId = PrivilegedRoleAdministrator, DisplayName = "Privileged Role Administrator" },
        new() { Id = SecurityAdministrator, TemplateId = SecurityAdministrator, DisplayName = "Security Administrator" },
        new() { Id = GlobalReader, TemplateId = GlobalReader, DisplayName = "Global Reader" },
        new() { Id = DirectoryReaders, TemplateId = DirectoryReaders, DisplayName = "Directory Readers" },
    ];

    public static string UserId(int n) => $"aaaaaaaa-0000-0000-0000-{n:D12}";

    public static DirectoryUser User(
        int n,
        bool enabled = true,
        bool? synced = null,
        int licences = 0,
        UserType type = UserType.Member) =>
        new()
        {
            Id = UserId(n),
            UserPrincipalName = $"user{n}@contoso.com",
            DisplayName = $"User {n}",
            UserType = type,
            AccountEnabled = enabled,
            OnPremisesSyncEnabled = synced,
            AssignedLicenceCount = licences,
        };

    public static DirectoryRoleAssignment Assign(string role, string principal) =>
        new() { RoleDefinitionId = role, PrincipalId = principal };

    public static FakeDirectoryReader Reader(
        IReadOnlyList<DirectoryUser> users,
        IReadOnlyList<DirectoryRoleAssignment> assignments,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? groups = null,
        IReadOnlyList<UserRegistration>? registrations = null) =>
        new(apps: [], userRegistrations: registrations ?? [])
        {
            Users = users,
            RoleAssignments = assignments,
            RoleDefinitions = Definitions,
            GroupMembers = groups ?? new Dictionary<string, IReadOnlyList<string>>(),
        };
}
