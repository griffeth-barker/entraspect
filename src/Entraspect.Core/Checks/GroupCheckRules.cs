namespace Entraspect.Core.Checks;

/// <summary>Rules shared by the group checks.</summary>
internal static class GroupCheckRules
{
    public const string ObjectType = "Group";

    // Named like the group list in the Entra admin center.
    public static readonly FindingLabels Labels = new()
    {
        ObjectDisplayName = "Display name",
        ObjectId = "Object ID",
        SubObjectDisplayName = null,
        SubObjectType = null,
        DueDate = null,
    };

    /// <summary>
    /// True when <paramref name="group"/> was created fewer than <see cref="CheckOptions.NewGroupGraceDays"/> UTC
    /// calendar days ago, so it may still be being set up. A group with no created date is never new.
    /// </summary>
    public static bool IsNew(DirectoryGroup group, CheckOptions options, DateTimeOffset now)
    {
        if (group.CreatedDateTime is not { } created)
        {
            return false;
        }

        var daysOld = (now.UtcDateTime.Date - created.UtcDateTime.Date).Days;
        return daysOld < options.NewGroupGraceDays;
    }

    /// <summary>
    /// The group kind as it starts a message, for example "Security group", or "Dynamic security group" when
    /// <paramref name="isDynamic"/> is true.
    /// </summary>
    public static string KindName(GroupKind kind, bool isDynamic = false)
    {
        var name = kind switch
        {
            GroupKind.Microsoft365 => "Microsoft 365 group",
            GroupKind.Security => "security group",
            GroupKind.MailEnabledSecurity => "mail-enabled security group",
            GroupKind.Distribution => "distribution group",
            _ => "group",
        };

        return isDynamic ? $"Dynamic {name}" : char.ToUpperInvariant(name[0]) + name[1..];
    }
}
