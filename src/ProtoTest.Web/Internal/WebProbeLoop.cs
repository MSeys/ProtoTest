namespace ProtoTest.Web.Internal;

using ProtoTest.Core;

/// <summary>
/// The web layer's retry loop: it probes until the observation satisfies the predicate or the timeout
/// elapses, polling every interval the session reports. Assertions and the Selenium backend share it, so
/// both retry at the same speed and report the same last observation. The interval is resolved per poll
/// from the backend's <see cref="IWebBackend.PollInterval"/>, so a configured interval reaches the
/// session's assertions instead of only the backend's own action retries.
/// </summary>
internal sealed class WebProbeLoop(Func<TimeSpan> interval)
{
    /// <summary>Polls a probe that reports whether its condition holds and what it observed. The two documented
    /// retryable web failures become observations rather than errors, so a locator that does not resolve
    /// yet or an element that is not actionable yet keeps the loop going.
    /// </summary>
    public ValueTask<ProtoPollResult<WebProbe>> PollObservationAsync(
        Func<CancellationToken, ValueTask<WebProbe>> probe,
        Func<WebProbe, bool> isSatisfied,
        TimeSpan timeout,
        CancellationToken cancellationToken)
        => PollAsync(
            async token =>
            {
                try
                {
                    return await probe(token);
                }
                catch (Exception exception) when (
                    exception is WebElementResolutionException or WebActionabilityException)
                {
                    return new WebProbe(false, exception.Message);
                }
            },
            isSatisfied,
            timeout,
            cancellationToken);

    /// <summary>Polls any probe until its observation satisfies the predicate.</summary>
    public ValueTask<ProtoPollResult<T>> PollAsync<T>(
        Func<CancellationToken, ValueTask<T>> probe,
        Func<T, bool> isSatisfied,
        TimeSpan timeout,
        CancellationToken cancellationToken)
        => ProtoPolling.PollAsync(probe, isSatisfied, timeout, interval(), cancellationToken);
}

/// <summary>What one probe observed: whether its condition holds and the text that explains the observation.</summary>
internal readonly record struct WebProbe(bool Holds, string Observation)
{
    /// <summary>Observes a condition with the text for each of its two outcomes.</summary>
    public WebProbe(bool holds, string whenHolds, string whenNotHolds)
        : this(holds, holds ? whenHolds : whenNotHolds)
    {
    }
}
