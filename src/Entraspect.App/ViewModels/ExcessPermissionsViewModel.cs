using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Entraspect.Core;

namespace Entraspect.App.ViewModels;

/// <summary>
/// Banners for granted permissions no check uses, from <see cref="PermissionReview"/>. Shown on Overview and
/// Settings > About while connected.
/// </summary>
public partial class ExcessPermissionsViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial bool HasWritePermissions { get; set; }

    [ObservableProperty]
    public partial string WriteMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasUnusedReadPermissions { get; set; }

    [ObservableProperty]
    public partial string ReadMessage { get; set; } = string.Empty;

    /// <summary>Updates the banners; null (not connected) hides them.</summary>
    public void SetReview(PermissionReview.Result? review)
    {
        var write = review?.UnusedWritePermissions ?? [];
        var read = review?.UnusedReadPermissions ?? [];

        WriteMessage = write.Count == 0 ? string.Empty : WriteText(write);
        HasWritePermissions = write.Count > 0;
        ReadMessage = read.Count == 0 ? string.Empty : ReadText(read);
        HasUnusedReadPermissions = read.Count > 0;
    }

    public static string WriteText(IReadOnlyList<string> permissions) =>
        $"This app registration has write permissions entraspect doesn't need: {string.Join(", ", permissions)}. " +
        "Anyone with its certificate can change your tenant. Remove them.";

    public static string ReadText(IReadOnlyList<string> permissions) =>
        $"This app registration has read permissions no entraspect check uses: {string.Join(", ", permissions)}. " +
        "Consider removing them, so the app registration only has what entraspect needs.";
}
