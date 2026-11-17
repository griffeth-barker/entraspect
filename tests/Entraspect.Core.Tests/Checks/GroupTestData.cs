namespace Entraspect.Core.Tests.Checks;

/// <summary>A test group with the counts <see cref="IDirectoryReader.GetGroupCountsAsync"/> returns for it.</summary>
internal sealed record TestGroup(DirectoryGroup Group, GroupCounts Counts);

internal static class GroupTestData
{
    public static readonly DateTimeOffset Now = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A group old enough to be checked, with members and owners unless overridden. <paramref name="hasOwners"/> true
    /// gives two owners; <paramref name="ownerCount"/>, when set, wins.
    /// </summary>
    public static TestGroup Group(
        string id,
        GroupKind kind = GroupKind.Security,
        bool isDynamic = false,
        bool isSynced = false,
        bool hasMembers = true,
        bool? hasOwners = true,
        DateTimeOffset? created = null,
        bool noCreatedDate = false,
        int? ownerCount = null,
        GroupVisibility? visibility = null,
        bool isPaused = false,
        IReadOnlyList<GroupSyncError>? syncErrors = null) =>
        new(
            new DirectoryGroup
            {
                Id = id,
                DisplayName = $"Group {id}",
                Kind = kind,
                IsDynamic = isDynamic,
                IsSyncedFromOnPremises = isSynced,
                IsRoleAssignable = false,
                CreatedDateTime = noCreatedDate ? null : created ?? Now.AddYears(-1),
                Visibility = visibility,
                IsDynamicProcessingPaused = isPaused,
                SyncErrors = syncErrors ?? [],
            },
            new GroupCounts
            {
                GroupId = id,
                HasMembers = hasMembers,
                OwnerCount = ownerCount ?? hasOwners switch
                {
                    true => 2,
                    false => 0,
                    null => null,
                },
            });

    /// <summary>The counts of <paramref name="groups"/>, by group ID, as <see cref="IDirectoryReader.GetGroupCountsAsync"/> returns them.</summary>
    public static IReadOnlyDictionary<string, GroupCounts> CountsOf(IEnumerable<TestGroup> groups) =>
        groups.ToDictionary(g => g.Group.Id, g => g.Counts, StringComparer.OrdinalIgnoreCase);
}
