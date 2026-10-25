namespace Entraspect.Core.Tests.Checks;

internal static class GroupTestData
{
    public static readonly DateTimeOffset Now = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A group old enough to be checked, with members and owners unless overridden. <paramref name="hasOwners"/> true
    /// gives two owners; <paramref name="ownerCount"/>, when set, wins.
    /// </summary>
    public static DirectoryGroup Group(
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
        new()
        {
            Id = id,
            DisplayName = $"Group {id}",
            Kind = kind,
            IsDynamic = isDynamic,
            IsSyncedFromOnPremises = isSynced,
            IsRoleAssignable = false,
            CreatedDateTime = noCreatedDate ? null : created ?? Now.AddYears(-1),
            HasMembers = hasMembers,
            OwnerCount = ownerCount ?? hasOwners switch
            {
                true => 2,
                false => 0,
                null => null,
            },
            Visibility = visibility,
            IsDynamicProcessingPaused = isPaused,
            SyncErrors = syncErrors ?? [],
        };
}
