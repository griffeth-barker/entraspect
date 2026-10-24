using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Entraspect.App.ViewModels;

public enum FilterKind
{
    /// <summary>Pick one or more values from a list, for example Severity.</summary>
    Choice,

    /// <summary>Type text that the value must contain, for example Display name.</summary>
    Text,
}

/// <summary>A finding property the user can filter on, named like its column.</summary>
public sealed class FilterField
{
    private readonly Func<IEnumerable<FindingRow>, IEnumerable<string>>? _choices;

    public FilterField(
        string name,
        FilterKind kind,
        Func<FindingRow, string?> value,
        Func<IEnumerable<FindingRow>, IEnumerable<string>>? choices = null)
    {
        Name = name;
        Kind = kind;
        Value = value;
        _choices = choices;
    }

    public string Name { get; }

    public FilterKind Kind { get; }

    public Func<FindingRow, string?> Value { get; }

    /// <summary>The values to choose from: a fixed list, or the distinct values in the current findings.</summary>
    public IReadOnlyList<string> ChoicesFor(IEnumerable<FindingRow> rows) =>
        (_choices?.Invoke(rows) ?? rows.Select(Value).OfType<string>().Distinct().Order()).ToList();
}

/// <summary>One option in a Choice filter's value list.</summary>
public partial class FilterChoice : ObservableObject
{
    public FilterChoice(string value, bool isSelected)
    {
        Value = value;
        IsSelected = isSelected;
    }

    public string Value { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>An applied filter, shown as a pill such as "Severity == Critical, Warning".</summary>
public sealed class ActiveFilter
{
    public ActiveFilter(FilterField field, IReadOnlyList<string> values, string text)
    {
        Field = field;
        Values = values;
        Text = text;
    }

    public FilterField Field { get; }

    /// <summary>The chosen values of a Choice filter; a row matches any of them.</summary>
    public IReadOnlyList<string> Values { get; }

    /// <summary>The text a Text filter's value must contain, ignoring case.</summary>
    public string Text { get; }

    public string DisplayText => Field.Kind == FilterKind.Choice
        ? $"{Field.Name} == {string.Join(", ", Values)}"
        : $"{Field.Name} contains \"{Text}\"";

    public bool Matches(FindingRow row)
    {
        var value = Field.Value(row);
        return Field.Kind == FilterKind.Choice
            ? value is not null && Values.Contains(value)
            : value is not null && value.Contains(Text, StringComparison.OrdinalIgnoreCase);
    }
}
