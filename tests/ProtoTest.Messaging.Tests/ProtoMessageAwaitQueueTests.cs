namespace ProtoTest.Messaging.Tests;

using ProtoTest.Messaging;

/// <summary>
/// The shared await machinery's own semantics, pinned without a broker: awaits on one consumer
/// serialize in call order, only messages at or after the consumer's position can match, a delivery
/// that matched no awaited predicate stays for a later await, a matched delivery is consumed once, the
/// deadline rescans once before it times out, and a cancelled await releases the consumer.
/// </summary>
[TestFixture]
public sealed class ProtoMessageAwaitQueueTests
{
    private const string Destination = "invoices";

    [Test]
    public async Task AwaitsOnOneQueue_ShouldSerializeInCallOrder()
    {
        var queue = new ProtoMessageAwaitQueue(0);
        var source = new Source();
        var first = queue.AwaitAsync(Destination, _ => true, TimeSpan.FromSeconds(10), default, () => source).AsTask();
        var second = queue.AwaitAsync(Destination, _ => true, TimeSpan.FromSeconds(10), default, () => source).AsTask();

        source.Add("first");
        source.Add("second");

        var received = await Task.WhenAll(first, second);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(received[0].Payload, Is.EqualTo("first"), "the await called first owns the message published first");
            Assert.That(received[1].Payload, Is.EqualTo("second"));
        }
    }

    [Test]
    public async Task UnmatchedDelivery_ShouldStayForALaterAwait()
    {
        var queue = new ProtoMessageAwaitQueue(0);
        var source = new Source();
        source.Add("lenient");

        var timeout = Assert.ThrowsAsync<TimeoutException>(async () =>
            await queue.AwaitAsync(Destination, message => message.Payload == "strict", TimeSpan.FromMilliseconds(150), default, () => source));

        var received = await queue.AwaitAsync(
            Destination,
            message => message.Payload == "lenient",
            TimeSpan.FromSeconds(1),
            default,
            () => source);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(timeout!.Message, Does.Contain(Destination), "the timeout names the destination");
            Assert.That(received.Payload, Is.EqualTo("lenient"), "a delivery that matched no awaited predicate is not consumed");
        }
    }

    [Test]
    public async Task MatchedDelivery_ShouldBeConsumedOnce()
    {
        var queue = new ProtoMessageAwaitQueue(0);
        var source = new Source();
        source.Add("first");
        source.Add("second");

        var first = await queue.AwaitAsync(Destination, _ => true, TimeSpan.FromSeconds(1), default, () => source);
        var second = await queue.AwaitAsync(Destination, _ => true, TimeSpan.FromSeconds(1), default, () => source);
        var timeout = Assert.ThrowsAsync<TimeoutException>(async () =>
            await queue.AwaitAsync(Destination, _ => true, TimeSpan.FromMilliseconds(100), default, () => source));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Payload, Is.EqualTo("first"));
            Assert.That(second.Payload, Is.EqualTo("second"));
            Assert.That(timeout, Is.Not.Null, "a matched message never matches a later await again");
        }
    }

    [Test]
    public async Task Destinations_ShouldNumberTheirDeliveriesIndependently()
    {
        var queue = new ProtoMessageAwaitQueue(0);
        // Two sources, each numbering its first delivery at position 1: the RabbitMQ shape, where
        // every destination's tap log starts at zero. A position alone does not identify a delivery
        // across sources, so the first await must not hide the second destination's first delivery.
        var invoices = new Source();
        var shipments = new Source();
        invoices.Add("first invoice", "invoices");
        shipments.Add("first shipment", "shipments");

        var invoice = await queue.AwaitAsync("invoices", _ => true, TimeSpan.FromSeconds(1), default, () => invoices);
        var shipment = await queue.AwaitAsync("shipments", _ => true, TimeSpan.FromSeconds(1), default, () => shipments);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(invoice.Payload, Is.EqualTo("first invoice"));
            Assert.That(
                shipment.Payload,
                Is.EqualTo("first shipment"),
                "one destination's consumed position never hides another destination's delivery");
        }
    }

    [Test]
    public async Task Deadline_ShouldRescanOnceBeforeTimingOut()
    {
        var queue = new ProtoMessageAwaitQueue(0);
        var source = new Source { PayloadAtDeadline = "late" };

        var received = await queue.AwaitAsync(Destination, message => message.Payload == "late", TimeSpan.FromMilliseconds(150), default, () => source);

        Assert.That(received.Payload, Is.EqualTo("late"),
            "a delivery that lands only when the deadline passes still wins, exactly like a RabbitMQ delivery assigned at the timeout");
    }

    [Test]
    public async Task CancelledAwait_ShouldThrowAndReleaseTheQueue()
    {
        var queue = new ProtoMessageAwaitQueue(0);
        var source = new Source();
        using var cancellation = new CancellationTokenSource();
        var pending = queue.AwaitAsync(Destination, _ => true, TimeSpan.FromSeconds(10), cancellation.Token, () => source).AsTask();

        cancellation.CancelAfter(50);
        Assert.CatchAsync<OperationCanceledException>(async () => await pending);

        source.Add("after");
        var received = await queue.AwaitAsync(Destination, _ => true, TimeSpan.FromSeconds(1), default, () => source);
        Assert.That(received.Payload, Is.EqualTo("after"), "a cancelled await releases the consumer for the next one");
    }

    [Test]
    public async Task EntriesBeforeThePosition_ShouldNeverMatch()
    {
        var source = new Source();
        source.Add("before");
        var queue = new ProtoMessageAwaitQueue(2);

        var timeout = Assert.ThrowsAsync<TimeoutException>(async () =>
            await queue.AwaitAsync(Destination, message => message.Payload == "before", TimeSpan.FromMilliseconds(100), default, () => source));

        source.Add("after");
        var received = await queue.AwaitAsync(Destination, _ => true, TimeSpan.FromSeconds(1), default, () => source);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(timeout, Is.Not.Null, "a message the consumer was created before can never satisfy its await");
            Assert.That(received.Payload, Is.EqualTo("after"));
        }
    }

    [Test]
    public async Task Reset_ShouldDropTheConsumedSetAndRebaseline()
    {
        var queue = new ProtoMessageAwaitQueue(1);
        var source = new Source();
        source.Add("first");
        _ = await queue.AwaitAsync(Destination, _ => true, TimeSpan.FromSeconds(1), default, () => source);

        // A replaced source starts over: its position 1 is a different delivery, so the old consumed
        // index must not hide it.
        queue.Reset(1);
        var received = await queue.AwaitAsync(Destination, _ => true, TimeSpan.FromSeconds(1), default, () => source);

        Assert.That(received.Payload, Is.EqualTo("first"));
    }

    /// <summary>
    /// A source over an append-only log, with the same signal-armed wait the in-memory broker uses. A
    /// payload configured for the deadline is only appended when a wait's remaining time has elapsed.
    /// </summary>
    private sealed class Source : IProtoMessageAwaitSource
    {
        private readonly List<ProtoMessageAwaitEntry> _entries = [];
        private TaskCompletionSource _changed = NewSignal();
        private long _next = 1;

        public string? PayloadAtDeadline { get; set; }

        public void Add(string payload, string destination = Destination)
        {
            TaskCompletionSource changed;
            lock (_entries)
            {
                _entries.Add(new ProtoMessageAwaitEntry(_next++, new ProtoMessage(destination, payload)));
                changed = _changed;
                _changed = NewSignal();
            }

            changed.TrySetResult();
        }

        public ValueTask<ProtoMessageAwaitSnapshot> SnapshotAsync(
            string destination,
            long position,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Task changed;
            List<ProtoMessageAwaitEntry> candidates = [];
            lock (_entries)
            {
                changed = _changed.Task;
                foreach (var entry in _entries)
                {
                    if (entry.Position >= position &&
                        string.Equals(entry.Message.Destination, destination, StringComparison.Ordinal))
                    {
                        candidates.Add(entry);
                    }
                }
            }

            return new(new ProtoMessageAwaitSnapshot(candidates, changed));
        }

        public async ValueTask WaitAsync(
            ProtoMessageAwaitSnapshot snapshot,
            TimeSpan remaining,
            CancellationToken cancellationToken)
        {
            if (PayloadAtDeadline is { } late)
            {
                PayloadAtDeadline = null;
                await Task.Delay(remaining, CancellationToken.None);
                Add(late);
                return;
            }

            var delay = Task.Delay(remaining, CancellationToken.None);
            var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = cancellationToken.Register(() => cancelled.TrySetResult());
            await Task.WhenAny(snapshot.Changed ?? delay, delay, cancelled.Task);
            cancellationToken.ThrowIfCancellationRequested();
        }

        private static TaskCompletionSource NewSignal()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
