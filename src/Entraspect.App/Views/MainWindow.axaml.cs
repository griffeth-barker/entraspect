using System.Threading.Tasks;
using Avalonia.Controls;
using Entraspect.App.Services;
using Entraspect.App.ViewModels;
using FluentAvalonia.UI.Controls;

namespace Entraspect.App.Views;

public partial class MainWindow : Window, IPasswordPrompt
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>Asks for the .pfx password in a dialog. The password is returned to the caller and never saved.</summary>
    public async Task<string?> RequestPasswordAsync(string profileName)
    {
        var passwordBox = new TextBox
        {
            PasswordChar = '•',
            PlaceholderText = "Certificate password",
            Width = 320,
        };

        var dialog = new FAContentDialog
        {
            Title = "Certificate password",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = string.IsNullOrWhiteSpace(profileName)
                            ? "Enter the password for the .pfx certificate. It is never saved."
                            : $"Enter the password for the .pfx certificate of '{profileName}'. It is never saved.",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        MaxWidth = 320,
                    },
                    passwordBox,
                },
            },
            PrimaryButtonText = "Connect",
            CloseButtonText = "Cancel",
            DefaultButton = FAContentDialogButton.Primary,
        };

        dialog.Opened += (_, _) => passwordBox.Focus();

        var result = await dialog.ShowAsync(this);
        var password = result == FAContentDialogResult.Primary ? passwordBox.Text ?? string.Empty : null;
        passwordBox.Text = string.Empty;
        return password;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Closing the window cancels a running scan or connect.
        (DataContext as MainViewModel)?.Shutdown();
        base.OnClosing(e);
    }
}
