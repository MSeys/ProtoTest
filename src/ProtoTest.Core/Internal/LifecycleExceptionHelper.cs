namespace ProtoTest.Core;

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
}
