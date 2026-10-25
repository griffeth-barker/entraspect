namespace Entraspect.Core;

/// <summary>A user's legacy per-user MFA state.</summary>
public enum PerUserMfaState
{
    /// <summary>The state couldn't be read, or Graph returned a value entraspect doesn't know.</summary>
    Unknown,
    Disabled,
    Enabled,
    Enforced,
}
