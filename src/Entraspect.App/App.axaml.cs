using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Entraspect.App.Services;
using Entraspect.App.ViewModels;
using Entraspect.App.Views;
using FluentAvalonia.Styling;

namespace Entraspect.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The window shows dialogs, the clipboard, the browser and the theme for the view models. The view model
            // applies the saved theme before the window shows.
            var window = new MainWindow();
            window.DataContext = new MainViewModel(ui: window);
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Applies the theme from Settings > Preferences. FluentAvalonia's PreferSystemTheme sets the theme variant from
    /// the operating system at startup and whenever the OS switches, so it is on only for "Use system setting";
    /// otherwise it would replace a light or dark choice when the OS switches.
    /// </summary>
    public static void ApplyTheme(AppTheme theme)
    {
        if (Current is not { } app)
        {
            return;
        }

        var fluent = app.Styles.OfType<FluentAvaloniaTheme>().FirstOrDefault();
        if (theme == AppTheme.System)
        {
            app.RequestedThemeVariant = ThemeVariant.Default;
            if (fluent is not null)
            {
                fluent.PreferSystemTheme = true;
            }

            return;
        }

        if (fluent is not null)
        {
            fluent.PreferSystemTheme = false;
        }

        app.RequestedThemeVariant = theme == AppTheme.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
    }
}
