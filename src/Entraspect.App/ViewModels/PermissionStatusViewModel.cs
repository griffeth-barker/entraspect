namespace Entraspect.App.ViewModels;

/// <summary>A required Microsoft Graph application permission and whether the connected app registration has it.</summary>
public sealed class PermissionStatusViewModel
{
    public PermissionStatusViewModel(string name, bool? isGranted)
    {
        Name = name;
        IsGranted = isGranted == true;
        IsMissing = isGranted == false;
    }

    public string Name { get; }

    public bool IsGranted { get; }

    public bool IsMissing { get; }

    /// <summary>"Granted", "Missing", or empty when not connected (granted permissions are unknown).</summary>
    public string StatusText => IsGranted ? "Granted" : IsMissing ? "Missing" : string.Empty;
}
