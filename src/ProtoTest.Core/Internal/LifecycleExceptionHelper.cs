namespace ProtoTest.Core.Internal;

using System.Runtime.ExceptionServices;

internal static class LifecycleExceptionHelper
{
    public static async Task CaptureAsync(Func<Task> action, List<Exception> exceptions)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            exceptions.Add(exception);
        }
    }

    public static void ThrowIfAny(string message, List<Exception> exceptions)
    {
        if (exceptions.Count == 1)
        {
            ExceptionDispatchInfo.Capture(exceptions[0]).Throw();
        }

        if (exceptions.Count > 1)
        {
            throw new AggregateException(message, exceptions);
        }
    }

    /// <summary>
    /// Disposes an object that may implement either disposal interface, preferring the asynchronous
    /// one; an object implementing neither is left alone.
    /// </summary>
    public static async ValueTask DisposeAsyncOrSync(object? disposable)
    {
        if (disposable is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else if (disposable is IDisposable syncDisposable)
        {
            syncDisposable.Dispose();
        }
    }

    /// <summary>
    /// The synchronous counterpart of <see cref="DisposeAsyncOrSync(object)"/>, for a failure path that
    /// cannot await and must not leak the scope.
    /// </summary>
    public static void DisposeOrSync(object? disposable)
    {
        if (disposable is IAsyncDisposable asyncDisposable)
        {
            asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        else if (disposable is IDisposable syncDisposable)
        {
            syncDisposable.Dispose();
        }
    }
}
