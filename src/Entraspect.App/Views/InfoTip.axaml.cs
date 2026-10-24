using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;

namespace Entraspect.App.Views;

/// <summary>
/// A small info icon whose tooltip holds an explanation. Pages keep descriptions and explainers here instead of in
/// body text.
/// </summary>
public partial class InfoTip : UserControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<InfoTip, string?>(nameof(Text));

    public InfoTip()
    {
        InitializeComponent();
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
        {
            var text = change.GetNewValue<string?>();
            ToolTip.SetTip(Icon, string.IsNullOrWhiteSpace(text)
                ? null
                : new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 360 });
            AutomationProperties.SetHelpText(Icon, text);
            IsVisible = !string.IsNullOrWhiteSpace(text);
        }
    }
}
