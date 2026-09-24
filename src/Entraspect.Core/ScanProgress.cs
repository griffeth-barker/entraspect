namespace Entraspect.Core;

/// <summary>Progress for a running scan, reported as each check is executed.</summary>
public sealed record ScanProgress(int Position, int Total, string CheckTitle)
{
    /// <summary>Human-readable progress text, for example "Running check 2 of 5: Expiring credentials".</summary>
    public string Message => $"Running check {Position} of {Total}: {CheckTitle}";
}
