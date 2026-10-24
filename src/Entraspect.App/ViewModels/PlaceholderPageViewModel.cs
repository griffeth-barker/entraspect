namespace Entraspect.App.ViewModels;

/// <summary>A page whose content arrives in a later step (Settings > Checks and About, spec 002 step 5).</summary>
public sealed class PlaceholderPageViewModel : ViewModelBase
{
    public PlaceholderPageViewModel(string title, string text)
    {
        Title = title;
        Text = text;
    }

    public string Title { get; }

    public string Text { get; }
}
