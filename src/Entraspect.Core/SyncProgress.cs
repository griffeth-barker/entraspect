namespace Entraspect.Core;

/// <summary>
/// An <see cref="IProgress{T}"/> that calls its handler at once on the reporting thread. Unlike
/// <see cref="Progress{T}"/> it doesn't post to a synchronization context, so reports keep their order and arrive
/// before the work that reported them goes on.
/// </summary>
public sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}
