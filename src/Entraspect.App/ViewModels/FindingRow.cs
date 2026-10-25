using Entraspect.App.Services;
using Entraspect.Core;

namespace Entraspect.App.ViewModels;

/// <summary>One row of the findings grid. Property names match <see cref="Finding"/> so column sorting works.</summary>
public sealed class FindingRow
{
    public FindingRow(Finding finding, TimeDisplay time)
    {
        Finding = finding;
        DueText = finding.DueDate is { } due ? time.Date(due) : string.Empty;
    }

    public Finding Finding { get; }

    public Severity Severity => Finding.Severity;

    public string SeverityText => Finding.Severity.ToString();

    public bool IsCritical => Finding.Severity == Severity.Critical;

    public bool IsWarning => Finding.Severity == Severity.Warning;

    public bool IsInfo => Finding.Severity == Severity.Info;

    public string ObjectDisplayName => Finding.ObjectDisplayName;

    public string? UserPrincipalName => Finding.UserPrincipalName;

    public string ObjectId => Finding.ObjectId;

    public string? SubObjectDisplayName => Finding.SubObjectDisplayName;

    public string? SubObjectType => Finding.SubObjectType;

    public string? Value => Finding.Value;

    public DateTimeOffset? DueDate => Finding.DueDate;

    /// <summary>The due date in the zone chosen in Settings > Preferences.</summary>
    public string DueText { get; }

    public string Message => Finding.Message;

    /// <summary>
    /// True if <paramref name="text"/> appears in the display name, user principal name, object ID, item, value or
    /// message.
    /// </summary>
    public bool MatchesSearch(string text) =>
        string.IsNullOrWhiteSpace(text)
        || Contains(ObjectDisplayName, text)
        || Contains(UserPrincipalName, text)
        || Contains(ObjectId, text)
        || Contains(SubObjectDisplayName, text)
        || Contains(Value, text)
        || Contains(Message, text);

    private static bool Contains(string? value, string text) =>
        value is not null && value.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase);
}
