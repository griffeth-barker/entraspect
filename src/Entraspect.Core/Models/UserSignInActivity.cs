namespace Entraspect.Core;

/// <summary>A user's most recent sign-ins. Each is null when the user never signed in that way, as far as Graph knows.</summary>
public sealed class UserSignInActivity
{
    public required string UserId { get; init; }

    /// <summary>The last successful sign-in. Microsoft started recording it in December 2023 and didn't backfill it.</summary>
    public DateTimeOffset? LastSuccessfulSignIn { get; init; }

    /// <summary>The last interactive sign-in attempt, successful or not.</summary>
    public DateTimeOffset? LastSignIn { get; init; }

    /// <summary>The last non-interactive sign-in attempt, successful or not.</summary>
    public DateTimeOffset? LastNonInteractiveSignIn { get; init; }
}
