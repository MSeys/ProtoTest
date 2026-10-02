namespace ProtoTest.Core;

using System.Diagnostics;

/// <summary>
/// Shared bounded polling: repeatedly probes until the observation satisfies the predicate or the
/// timeout elapses, returning the last observation and whether it was satisfied. The caller decides
/// what a timeout means, so the same loop serves assertions, synchronization waits and readiness
/// probes without owning their failure semantics.
/// </summary>
public static class ProtoPolling
{
    /// <summary>The default interval between probes when a caller does not specify one.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Probes until <paramref name="isSatisfied"/> returns true or <paramref name="timeout"/> elapses.
    /// </summary>
    public static async ValueTask<ProtoPollResult<T>> PollAsync<T>(
        Func<CancellationToken, ValueTask<T>> probe,
        Func<T, bool> isSatisfied,
        TimeSpan timeout,
        TimeSpan pollInterval,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(isSatisfied);
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        if (pollInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(pollInterval));

        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var observation = await probe(cancellationToken).ConfigureAwait(false);
            if (isSatisfied(observation))
            {
                return new ProtoPollResult<T>(observation, Satisfied: true, stopwatch.Elapsed);
            }

            if (stopwatch.Elapsed >= timeout)
            {
                return new ProtoPollResult<T>(observation, Satisfied: false, stopwatch.Elapsed);
            }

            // Rounded up to whole milliseconds: Task.Delay truncates a sub-millisecond span to zero,
            // which would spin the probe until the deadline.
            var remaining = TimeSpan.FromMilliseconds(Math.Ceiling((timeout - stopwatch.Elapsed).TotalMilliseconds));
            await Task.Delay(remaining < pollInterval ? remaining : pollInterval, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}

/// <summary>The outcome of a <see cref="ProtoPolling.PollAsync{T}"/> call.</summary>
public readonly record struct ProtoPollResult<T>(T Value, bool Satisfied, TimeSpan Elapsed);
