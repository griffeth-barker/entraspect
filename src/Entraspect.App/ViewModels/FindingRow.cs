using Entraspect.Core;

namespace Entraspect.App.ViewModels;

/// <summary>One row of the findings grid. Property names match <see cref="Finding"/> so column sorting works.</summary>
public sealed class FindingRow
{
    public FindingRow(Finding finding)
    {
        Finding = finding;
    }

    public Finding Finding { get; }

    public Severity Severity => Finding.Severity;

    public string SeverityText => Finding.Severity.ToString();

    public bool IsCritical => Finding.Severity == Severity.Critical;

    public bool IsWarning => Finding.Severity == Severity.Warning;

    public bool IsInfo => Finding.Severity == Severity.Info;

    public string ObjectDisplayName => Finding.ObjectDisplayName;

    public string? SubObjectDisplayName => Finding.SubObjectDisplayName;

    public string? SubObjectType => Finding.SubObjectType;

    public DateTimeOffset? DueDate => Finding.DueDate;

    public string Message => Finding.Message;

    /// <summary>True if <paramref name="text"/> appears in the object, item or message, ignoring case.</summary>
    public bool Matches(string text) =>
        string.IsNullOrWhiteSpace(text)
        || Contains(ObjectDisplayName, text)
        || Contains(SubObjectDisplayName, text)
        || Contains(Message, text);

    private static bool Contains(string? value, string text) =>
        value is not null && value.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase);
}
