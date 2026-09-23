namespace Entraspect.Core;

/// <summary>
/// Thrown by <see cref="IDirectoryReader"/> implementations when the directory cannot be read.
/// Lets callers show errors without depending on the Graph SDK.
/// </summary>
public sealed class DirectoryReadException : Exception
{
    public DirectoryReadException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>HTTP status code from the directory service, or null if no response was received.</summary>
    public int? StatusCode { get; }
}
