namespace ProtoTest.Web.Internal;

using System.Diagnostics;

/// <summary>
/// Shared bounded polling used by web assertions and synchronization waits.
/// </summary>
internal static class WebPolling
{
    /// <summary>The default interval between probes when a caller does not specify one.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Repeatedly probes until <paramref name="isSatisfied"/> returns true or <paramref name="timeout"/>
    /// elapses. Returns the last observation and whether it was satisfied; the caller decides what to throw.
    /// </summary>
    public static async ValueTask<WebPollResult<T>> PollAsync<T>(
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
                return new WebPollResult<T>(observation, Satisfied: true, stopwatch.Elapsed);
            }

            if (stopwatch.Elapsed >= timeout)
            {
                return new WebPollResult<T>(observation, Satisfied: false, stopwatch.Elapsed);
            }

            var remaining = timeout - stopwatch.Elapsed;
            await Task.Delay(remaining < pollInterval ? remaining : pollInterval, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}

/// <summary>The outcome of a <see cref="WebPolling.PollAsync{T}"/> call.</summary>
internal readonly record struct WebPollResult<T>(T Value, bool Satisfied, TimeSpan Elapsed);
