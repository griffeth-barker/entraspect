namespace Entraspect.App.ViewModels;

/// <summary>A Microsoft Graph application permission and whether the connected app registration has it.</summary>
public sealed class PermissionStatusViewModel
{
    public const string LicenceToolTip =
        "Grant it so entraspect can tell whether your tenant's licence covers this check.";

    /// <param name="isOptional">
    /// True for a permission the check runs without (the licence permission): neutral rather than red when missing.
    /// </param>
    public PermissionStatusViewModel(string name, bool? isGranted, bool isOptional = false)
    {
        Name = name;
        IsOptional = isOptional;
        IsGranted = isGranted == true;
        IsMissing = isGranted == false && !isOptional;
    }

    public bool IsOptional { get; }

    public string Name { get; }

    public bool IsGranted { get; }

    public bool IsMissing { get; }

    public string ToolTipText => IsGranted
        ? "Granted to the connected app registration."
        : IsOptional
            ? LicenceToolTip
            : IsMissing
            ? "Not granted to the connected app registration. Add it and grant admin consent, then reconnect."
            : "Connect to see whether this permission is granted.";
}
