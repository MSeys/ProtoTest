namespace ProtoTest.Messaging.RabbitMq;

using System.Text;
using System.Threading.Channels;
using global::RabbitMQ.Client;
using global::RabbitMQ.Client.Events;
using global::RabbitMQ.Client.Exceptions;
using ProtoTest.Messaging;

/// <summary>
/// One test's RabbitMQ consumer. An exchange destination gets an exclusive, auto-delete tap queue on
/// its own channel on the run's shared connection, bound with the destination as routing key and with
/// "#", so an await reacts to a delivery instead of polling; a queue destination
/// (<see cref="ProtoDestination.Queue"/>) is consumed directly instead, and its deliveries carry the
/// queue destination and the transport's routing key. Each queue is fed by an asynchronous consumer
/// into an unbounded channel, so an await reacts to a delivery instead of polling. A channel per
/// destination keeps one destination that cannot be prepared - a missing exchange or queue closes its
/// channel - from poisoning the destinations that were already prepared. The destinations named in
/// one prepare are prepared concurrently: each tap declares on its own channel, so their broker
/// round trips overlap and every destination is still attempted. Prepared destinations are
/// bound before the act; a destination that was never prepared is prepared just in time at the first
/// await, which only sees messages published after the await begins (a queue consume reads the
/// queue's backlog as well). The base consumer owns the await queue, so awaits serialize in call
/// order and a delivery that matched no awaited predicate stays for a later await. Taps are deleted
/// when the consumer is disposed with the test and an exclusive queue never competes with the
/// application's own consumers; a consumed queue is left as it is and only the consumer's channel is
/// released.
/// </summary>
internal sealed class RabbitMqProtoMessageConsumer : ProtoMessageConsumerBase, IProtoMessageConsumer
{
    private readonly RabbitMqMessageBroker _broker;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, Tap> _taps = new(StringComparer.Ordinal);
    private bool _disposed;

    public RabbitMqProtoMessageConsumer(RabbitMqMessageBroker broker)
        : base(0)
    {
        ArgumentNullException.ThrowIfNull(broker);
        _broker = broker;
    }

    public override async ValueTask PrepareAsync(
        IReadOnlyCollection<string> destinations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destinations);

        // The destinations are prepared concurrently: each tap owns its channel, so their round trips
        // overlap and one destination that cannot be prepared (a missing exchange or queue) closes only
        // its own channel. Every destination is attempted; Task.WhenAll rethrows the first failure once
        // all of them have finished.
        var requested = destinations
            .Where(destination => !string.IsNullOrWhiteSpace(destination))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        await Task.WhenAll(requested.Select(destination => PrepareOneAsync(destination, cancellationToken)))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Prepares one destination. The declare runs outside the consumer's gate so prepares for
    /// different destinations overlap; registration takes the gate again, so a race with a just-in-time
    /// await resolves to one registered tap and the extra queue is released.
    /// </summary>
    private async Task PrepareOneAsync(string destination, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_taps.ContainsKey(destination))
            {
                return;
            }
        }
        finally
        {
            _gate.Release();
        }

        var tap = await DeclareAsync(destination, cancellationToken).ConfigureAwait(false);
        try
        {
            await RegisterTapAsync(destination, tap, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The consumer was disposed while the tap declared: the tap never reached the registry,
            // so its queue and channel are released here.
            await DisposeTapAsync(tap).ConfigureAwait(false);
            throw;
        }
    }

    private async Task RegisterTapAsync(string destination, Tap tap, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        Tap? extra = null;
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_taps.TryAdd(destination, tap))
            {
                extra = tap;
            }
        }
        finally
        {
            _gate.Release();
        }

        if (extra is not null)
        {
            // A just-in-time await prepared the same destination while this declare ran: the registered
            // tap keeps the deliveries and the extra queue is released.
            await DisposeTapAsync(extra).ConfigureAwait(false);
        }
    }

    public override ValueTask<ProtoMessage> AwaitAsync(
        string destination,
        Func<ProtoMessage, bool> predicate,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
        => AwaitAsync(destination, routingKey: null, predicate, timeout, cancellationToken);

    /// <summary>
    /// Waits on the destination's tap, binding <paramref name="routingKey"/> on it when the await names
    /// one: a direct exchange then delivers a message under that key, where the prepared destination and
    /// "#" bindings only cover the destination key and topic catch-alls. The tap is the one per
    /// destination, so a destination prepared with <c>Tap</c> still holds what was published before this
    /// await, and the base queue filters the candidates by the composed predicate. A queue destination
    /// has no exchange to bind: the key filters the deliveries the consumed queue hands over.
    /// </summary>
    public async ValueTask<ProtoMessage> AwaitAsync(
        string destination,
        string? routingKey,
        Func<ProtoMessage, bool> predicate,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(predicate);
        Tap tap;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_taps.TryGetValue(destination, out var existing))
            {
                tap = existing;
            }
            else
            {
                tap = await DeclareAsync(destination, cancellationToken).ConfigureAwait(false);
                _taps.Add(destination, tap);
            }

            if (routingKey is not null && !ProtoDestination.IsQueue(destination))
            {
                await tap.BindAsync(destination, routingKey, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }

        // The base consumer owns the serialization and the consumed set; the source scans the tap's log
        // and reads new deliveries from its channel.
        return await AwaitAsync(
            destination,
            MatchRoutingKey(predicate, routingKey),
            timeout,
            cancellationToken,
            () => new Source(tap)).ConfigureAwait(false);
    }

    public override async ValueTask DisposeAsync()
    {
        Tap[] taps;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            taps = [.. _taps.Values];
            _taps.Clear();
        }
        finally
        {
            _gate.Release();
        }

        // Taps are independent - one channel and one queue each - so they are released together
        // instead of one round trip after another; every tap is still attempted and a failure in one
        // never stops the others.
        await Task.WhenAll(taps.Select(DisposeTapAsync)).ConfigureAwait(false);
    }

    private static async Task DisposeTapAsync(Tap tap)
    {
        try
        {
            if (tap.OwnsQueue && tap.Channel.IsOpen)
            {
                await tap.Channel.QueueDeleteAsync(tap.Queue, ifUnused: false, ifEmpty: false).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // Cleanup only: a queue that is already gone must not fail the test's teardown.
        }

        try
        {
            await tap.Channel.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Cleanup only: a channel the broker already closed must not fail the test's teardown.
        }
    }

    /// <summary>
    /// Prepares one destination: a queue destination is consumed as it exists, an exchange destination
    /// gets the test's own tap queue bound to it.
    /// </summary>
    private async Task<Tap> DeclareAsync(string destination, CancellationToken cancellationToken)
        => ProtoDestination.IsQueue(destination)
            ? await ConsumeQueueAsync(destination, cancellationToken).ConfigureAwait(false)
            : await DeclareExchangeTapAsync(destination, cancellationToken).ConfigureAwait(false);

    private async Task<Tap> DeclareExchangeTapAsync(string destination, CancellationToken cancellationToken)
    {
        // One channel per tap: a destination whose exchange is missing closes its own channel on the
        // 404, so the taps already prepared for other destinations keep receiving.
        var channel = await _broker.CreateChannelAsync(cancellationToken).ConfigureAwait(false);
        string queue;
        try
        {
            queue = (await channel.QueueDeclareAsync(
                queue: $"prototest-{Guid.NewGuid():N}",
                durable: false,
                exclusive: true,
                autoDelete: true,
                arguments: null,
                cancellationToken: cancellationToken).ConfigureAwait(false)).QueueName;

            // Direct exchanges match the destination routing key; "#" keeps topic exchanges catch-all;
            // fanout and headers exchanges ignore the routing key and match these argument-less bindings.
            // RabbitMQ still delivers one copy per message even when both bindings match this queue.
            await channel.QueueBindAsync(queue, destination, routingKey: destination, arguments: null, cancellationToken: cancellationToken).ConfigureAwait(false);
            await channel.QueueBindAsync(queue, destination, routingKey: "#", arguments: null, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationInterruptedException exception) when (exception.ShutdownReason is { ReplyCode: 404 })
        {
            await DisposeQuietlyAsync(channel).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"Cannot await messages on '{destination}': the exchange '{destination}' does not exist on the broker. " +
                "Declare the exchange before the test awaits it.",
                exception);
        }
        catch (Exception)
        {
            await DisposeQuietlyAsync(channel).ConfigureAwait(false);
            throw;
        }

        var deliveries = Channel.CreateUnbounded<ProtoMessage>(new UnboundedChannelOptions
        {
            // Awaits serialize on the consumer's await queue, but the channel stays honest without it:
            // a second reader must be possible, because a silent SingleReader violation would let a
            // racing await consume the delivery another await owned.
            SingleReader = false,
            SingleWriter = false
        });
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) =>
        {
            // The delivery's body memory is only valid while the handler runs, so the message is
            // converted here instead of holding the event args for a later await.
            deliveries.Writer.TryWrite(Convert(delivery));
            return Task.CompletedTask;
        };
        try
        {
            await channel.BasicConsumeAsync(
                queue,
                autoAck: true,
                consumerTag: string.Empty,
                noLocal: false,
                exclusive: false,
                arguments: null,
                consumer: consumer,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await DisposeQuietlyAsync(channel).ConfigureAwait(false);
            throw;
        }

        var tap = new Tap(queue, deliveries.Reader, channel, destination, ownsQueue: true);
        return tap;
    }

    /// <summary>
    /// Consumes a named queue: the queue exists through whoever owns it - the application's topology,
    /// or the dead-letter bindings that feed a dead-letter queue - so a passive declaration verifies it
    /// without creating or deleting anything, and the consumer reads the queue's backlog as well as
    /// what arrives later. The queue stays untouched when the test ends; only the consumer's channel is
    /// released. Unmatched deliveries stay in the tap's log for a later await on the same consumer, but
    /// they are already removed from the queue, so a second test awaiting the same queue no longer sees
    /// them - a queue with a live reader is inherently shared.
    /// </summary>
    private async Task<Tap> ConsumeQueueAsync(string destination, CancellationToken cancellationToken)
    {
        var queue = ProtoDestination.QueueName(destination);

        // One channel per tap, like the exchange path: a missing queue closes its own channel, so the
        // destinations already prepared keep receiving.
        var channel = await _broker.CreateChannelAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await channel.QueueDeclarePassiveAsync(queue, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationInterruptedException exception) when (exception.ShutdownReason is { ReplyCode: 404 })
        {
            await DisposeQuietlyAsync(channel).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"Cannot await messages on '{destination}': the queue '{queue}' does not exist on the broker. " +
                "A queue destination is consumed as its owner feeds it; a dead-letter queue exists once the " +
                "dead-letter bindings do.",
                exception);
        }
        catch (Exception)
        {
            await DisposeQuietlyAsync(channel).ConfigureAwait(false);
            throw;
        }

        var deliveries = Channel.CreateUnbounded<ProtoMessage>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) =>
        {
            // The delivery's body memory is only valid while the handler runs, so the message is
            // converted here instead of holding the event args for a later await. The queue is the
            // address the await named; the transport's routing key stays on the message.
            deliveries.Writer.TryWrite(Convert(delivery, destination));
            return Task.CompletedTask;
        };
        try
        {
            await channel.BasicConsumeAsync(
                queue,
                autoAck: true,
                consumerTag: string.Empty,
                noLocal: false,
                exclusive: false,
                arguments: null,
                consumer: consumer,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await DisposeQuietlyAsync(channel).ConfigureAwait(false);
            throw;
        }

        var tap = new Tap(queue, deliveries.Reader, channel, destination, ownsQueue: false);
        return tap;
    }

    private static async ValueTask DisposeQuietlyAsync(IChannel channel)
    {
        try
        {
            await channel.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Cleanup only: the broker may already have closed the channel on the error path.
        }
    }

    private static ProtoMessage Convert(BasicDeliverEventArgs delivery, string? destination = null)
    {
        var properties = delivery.BasicProperties;
        var headers = properties.Headers is null
            ? null
            : properties.Headers
                .ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value switch
                    {
                        byte[] bytes => Encoding.UTF8.GetString(bytes),
                        null => null,
                        var value => value.ToString()
                    },
                    StringComparer.OrdinalIgnoreCase);
        var address = destination
            ?? (string.IsNullOrEmpty(delivery.Exchange) ? delivery.RoutingKey : delivery.Exchange);
        return new ProtoMessage(
            address,
            Encoding.UTF8.GetString(delivery.Body.Span),
            headers,
            properties.ContentType)
        {
            RoutingKey = delivery.RoutingKey
        };
    }

    /// <summary>
    /// One destination's tap: the queue declared for the test (an exclusive tap queue) or consumed as
    /// it exists (a named queue, <see cref="OwnsQueue"/> false), its delivery channel and channel, the
    /// routing keys already bound on it, and the log of every delivery the tap has read. The log is
    /// append-only and positions are the log indices; a delivery that matched an await is consumed by
    /// the shared queue, so an unmatched one stays for a later await.
    /// </summary>
    private sealed class Tap(
        string queue,
        ChannelReader<ProtoMessage> deliveries,
        IChannel channel,
        string destination,
        bool ownsQueue)
    {
        private readonly HashSet<string> _bound = new(StringComparer.Ordinal) { destination, "#" };

        public string Queue { get; } = queue;

        public ChannelReader<ProtoMessage> Deliveries { get; } = deliveries;

        public IChannel Channel { get; } = channel;

        /// <summary>Whether the queue belongs to the test and is deleted with the consumer.</summary>
        public bool OwnsQueue { get; } = ownsQueue;

        public List<ProtoMessage> Log { get; } = [];

        public void Append(ProtoMessage message) => Log.Add(message);

        /// <summary>
        /// Binds one routing key on the tap's queue, once; a direct exchange then delivers messages
        /// published under it. The destination and "#" bindings are declared with the tap itself. A
        /// consumed queue has no exchange binding to add: the key filters its deliveries instead.
        /// </summary>
        public async ValueTask BindAsync(string exchange, string routingKey, CancellationToken cancellationToken)
        {
            if (!OwnsQueue || !_bound.Add(routingKey))
            {
                return;
            }

            await Channel.QueueBindAsync(
                Queue,
                exchange,
                routingKey: routingKey,
                arguments: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The tap's log and channel as an await source: a snapshot reads the log, and the wait reads one
    /// delivery from the channel within the remaining time, appending whatever it read - including a
    /// delivery assigned at the same instant as the deadline, which the queue's post-deadline scan
    /// still sees.
    /// </summary>
    private sealed class Source(Tap tap) : IProtoMessageAwaitSource
    {
        public ValueTask<ProtoMessageAwaitSnapshot> SnapshotAsync(
            string destination,
            long position,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var log = tap.Log;
            var candidates = new List<ProtoMessageAwaitEntry>(log.Count);
            for (var index = 0; index < log.Count; index++)
            {
                if (index >= position)
                {
                    candidates.Add(new ProtoMessageAwaitEntry(index, log[index]));
                }
            }

            return new(new ProtoMessageAwaitSnapshot(candidates, Changed: null));
        }

        public async ValueTask WaitAsync(
            ProtoMessageAwaitSnapshot snapshot,
            TimeSpan remaining,
            CancellationToken cancellationToken)
        {
            using var expiry = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            expiry.CancelAfter(remaining);
            try
            {
                tap.Append(await tap.Deliveries.ReadAsync(expiry.Token).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // A delivery assigned at the same instant the deadline passes must win, never be
                // dropped: append whatever the channel still holds, so the queue's post-deadline scan
                // gives it its chance before the await times out.
                if (tap.Deliveries.TryRead(out var late))
                {
                    tap.Append(late);
                }
            }
        }
    }
}
