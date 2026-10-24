using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Entraspect.App.Services;
using Entraspect.App.ViewModels;
using FluentAvalonia.UI.Controls;

namespace Entraspect.App.Views;

public partial class MainWindow : Window, IUserInteraction
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MainViewModel vm)
        {
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.SelectedNavItem))
                {
                    Dispatcher.UIThread.Post(() => MarkSelectedContainer(vm.SelectedNavItem), DispatcherPriority.Loaded);
                }
            };
        }
    }

    /// <summary>
    /// Selecting an item inside a collapsed section expands the section, but NavigationView creates the item's
    /// container after it applied the selection, so the container shows the selection marker without the selected
    /// background. Once layout has created the container, mark it selected.
    /// </summary>
    private void MarkSelectedContainer(NavItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        var container = Nav.GetVisualDescendants()
            .OfType<FANavigationViewItem>()
            .FirstOrDefault(c => ReferenceEquals(c.DataContext, item));

        if (container is { IsSelected: false })
        {
            container.IsSelected = true;
        }
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

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        var dialog = new FAContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 420 },
            PrimaryButtonText = confirmText,
            CloseButtonText = "Cancel",
            DefaultButton = FAContentDialogButton.Close,
        };

        return await dialog.ShowAsync(this) == FAContentDialogResult.Primary;
    }

    public async Task CopyTextAsync(string text)
    {
        if (Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    public async Task OpenUrlAsync(string url)
    {
        await Launcher.LaunchUriAsync(new Uri(url));
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Closing the window cancels a running scan or connect.
        (DataContext as MainViewModel)?.Shutdown();
        base.OnClosing(e);
    }
}
