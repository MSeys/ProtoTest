namespace ProtoTest.Messaging.MassTransit.Internal;

using global::MassTransit.Testing;
using ProtoTest.Core;
using ProtoTest.Messaging;
using ProtoTest.Messaging.Internal;

/// <summary>
/// One test's view of the harness. The harness keeps every published message for the whole run, so the
/// consumer snapshots the published position at its first use - during setup for a declared destination,
/// at the first await otherwise - and only ever matches later messages. The shared await queue
/// serializes awaits in call order and leaves a message that matched no awaited predicate for a later
/// await, so concurrent awaits neither lose nor steal each other's messages.
/// </summary>
internal sealed class MassTransitProtoMessageConsumer<TProgram>(MassTransitMessageBroker<TProgram> broker) : IProtoMessageConsumer
    where TProgram : class
{
    private readonly MassTransitMessageBroker<TProgram> _broker = broker;
    private readonly ProtoMessageAwaitQueue _queue = new(0);
    private readonly ProtoLock _gate = new();
    private ITestHarness? _harness;

    public ValueTask PrepareAsync(
        IReadOnlyCollection<string> destinations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destinations);
        var harness = _broker.Harness();
        EnsurePosition(harness);

        // A destination that names no loaded message contract fails its own awaits with the named error
        // - the initializer records it per destination - instead of waiting for a match that cannot come.
        foreach (var destination in destinations)
        {
            if (!string.IsNullOrWhiteSpace(destination))
            {
                MassTransitMessages.Resolve(destination, typeof(TProgram).Assembly);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public async ValueTask<ProtoMessage> AwaitAsync(
        string destination,
        Func<ProtoMessage, bool> predicate,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(predicate);

        // The harness is resolved inside the await gate, so a substitution that changed it since the
        // last await re-baselines the consumer without racing an await that is already in flight.
        return await _queue.AwaitAsync(destination, predicate, timeout, cancellationToken, () =>
        {
            var harness = _broker.Harness();
            EnsurePosition(harness);
            return new Source(harness);
        }).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>
    /// Pins the position the consumer starts from in <paramref name="harness"/>: the number of messages
    /// the bus had published when this test first reached the harness. A message published before it
    /// stays another test's (or an earlier test's) message, exactly like the in-memory broker's
    /// creation-time snapshot.
    /// A substitution mid-test replaces the run's server with this test's dedicated one, hence a fresh
    /// harness whose history starts empty: the position restarts at zero there, because every message
    /// on that harness was published for this test, and the replaced harness's consumed indices die
    /// with it.
    /// </summary>
    private void EnsurePosition(ITestHarness harness)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_harness, harness))
            {
                return;
            }

            _queue.Reset(_harness is null ? harness.Published.Count(CancellationToken.None) : 0);
            _harness = harness;
        }
    }

    /// <summary>
    /// The harness's published history as an await source: a snapshot returns the messages at or after
    /// the awaited position that the destination's contract can match, and the wait polls at the shared
    /// interval, because the harness offers no signal for a publish.
    /// </summary>
    private sealed class Source(ITestHarness harness) : IProtoMessageAwaitSource
    {
        public async ValueTask<ProtoMessageAwaitSnapshot> SnapshotAsync(
            string destination,
            long position,
            CancellationToken cancellationToken)
        {
            List<ProtoMessageAwaitEntry> candidates = [];
            long index = 0;
            await foreach (var published in harness.Published.SelectAsync(_ => { }, cancellationToken).ConfigureAwait(false))
            {
                var current = index++;
                if (current < position || !MassTransitMessages.Matches(destination, published.MessageType))
                {
                    continue;
                }

                candidates.Add(new ProtoMessageAwaitEntry(current, MassTransitMessages.ToProtoMessage(published)));
            }

            return new ProtoMessageAwaitSnapshot(candidates, Changed: null);
        }

        public ValueTask WaitAsync(
            ProtoMessageAwaitSnapshot snapshot,
            TimeSpan remaining,
            CancellationToken cancellationToken)
            => new(Task.Delay(
                remaining < ProtoPolling.DefaultInterval ? remaining : ProtoPolling.DefaultInterval,
                cancellationToken));
    }
}
