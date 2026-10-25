namespace Entraspect.Core;

/// <summary>A user's successful single-factor interactive sign-ins in a period: how many, and the most recent.</summary>
public sealed record SingleFactorSignIns(int Count, DateTimeOffset LastSignIn);
