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

    public string ToolTipText => IsGranted
        ? "Granted to the connected app registration."
        : IsMissing
            ? "Not granted to the connected app registration. Add it and grant admin consent, then reconnect."
            : "Connect to see whether this permission is granted.";
}
