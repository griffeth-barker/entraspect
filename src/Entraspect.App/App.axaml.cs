using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Entraspect.App.ViewModels;
using Entraspect.App.Views;

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
            // The window shows the certificate password dialog, so the view model asks it for passwords.
            var window = new MainWindow();
            window.DataContext = new MainViewModel(passwordPrompt: window);
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
