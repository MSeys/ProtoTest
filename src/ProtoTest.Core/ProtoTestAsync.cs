namespace ProtoTest.Core;

/// <summary>
/// The one bridge for frameworks whose lifecycle hooks are synchronous. The frameworks call these
/// hooks on a thread without a synchronization context and Core's awaits use ConfigureAwait(false),
/// so blocking here cannot deadlock the way a UI-thread wait could.
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
