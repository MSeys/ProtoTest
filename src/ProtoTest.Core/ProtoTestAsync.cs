namespace ProtoTest.Core;

/// <summary>
/// The one bridge for frameworks whose lifecycle hooks are synchronous. It blocks the calling thread
/// until the async lifecycle completes; the adapters only use it on runner hooks that do not install a
/// single-threaded synchronization context, where a blocking wait cannot deadlock.
/// </summary>
public static class ProtoTestAsync
{
    /// <summary>Blocks until the async lifecycle call completes and rethrows its failure.</summary>
    public static void RunSync(Func<ValueTask> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        operation().GetAwaiter().GetResult();
    }

    /// <summary>Blocks until the async lifecycle call completes and returns its result.</summary>
    public static T RunSync<T>(Func<ValueTask<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return operation().GetAwaiter().GetResult();
    }
}
