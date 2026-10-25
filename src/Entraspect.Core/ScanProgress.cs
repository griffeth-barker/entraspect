namespace Entraspect.Core;

/// <summary>
/// Progress for a running scan, reported as each check is executed, and again with <paramref name="Detail"/> by checks
/// that report how far they are, for example "looking up user 4 of 20".
/// </summary>
public sealed record ScanProgress(int Position, int Total, string CheckTitle, string? Detail = null)
{
    /// <summary>
    /// Human-readable progress text, for example "Running check 2 of 5: Expiring credentials", with the detail in
    /// parentheses when there is one.
    /// </summary>
    public string Message =>
        Detail is null
            ? $"Running check {Position} of {Total}: {CheckTitle}"
            : $"Running check {Position} of {Total}: {CheckTitle} ({Detail})";
}
