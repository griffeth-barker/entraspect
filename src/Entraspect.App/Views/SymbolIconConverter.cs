using System.Globalization;
using Avalonia.Data.Converters;
using FluentAvalonia.UI.Controls;

namespace Entraspect.App.Views;

/// <summary>Turns a FluentAvalonia symbol name from a view model into an icon, or no icon for null or unknown names.</summary>
public sealed class SymbolIconConverter : IValueConverter
{
    public static readonly SymbolIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string name && Enum.TryParse<FASymbol>(name, out var symbol)
            ? new FASymbolIconSource { Symbol = symbol }
            : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
