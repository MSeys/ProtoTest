namespace ProtoTest.Messaging.RabbitMq;

using System.Text;
using System.Threading.Channels;
using global::RabbitMQ.Client;
using global::RabbitMQ.Client.Events;
using global::RabbitMQ.Client.Exceptions;
using ProtoTest.Messaging;

/// <summary>
/// One test's RabbitMQ consumer: an exclusive, auto-delete tap queue per destination, each on its own
/// channel on the run's shared connection, and an asynchronous consumer per queue feeding an unbounded
/// channel, so an await reacts to a delivery instead of polling. A channel per tap keeps one destination
/// that cannot be declared - a missing exchange closes its channel - from poisoning the taps that were
/// already prepared. Prepared queues are declared before the act; a destination that was never prepared
/// is declared just in time at the first await, which only sees messages published after the await
/// begins. The base consumer owns the await queue, so awaits serialize in call order and a delivery
/// that matched no awaited predicate stays for a later await. All queues are deleted when the consumer
/// is disposed with the test, and an exclusive queue never competes with the application's own
/// consumers.
/// </summary>
internal sealed class RabbitMqProtoMessageConsumer : ProtoMessageConsumerBase
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
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            foreach (var destination in destinations)
            {
                if (!string.IsNullOrWhiteSpace(destination) && !_taps.ContainsKey(destination))
                {
                    await DeclareAsync(destination, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public override async ValueTask<ProtoMessage> AwaitAsync(
        string destination,
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
            tap = _taps.TryGetValue(destination, out var existing)
                ? existing
                : await DeclareAsync(destination, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        // The base consumer owns the serialization and the consumed set; the source scans the tap's log
        // and reads new deliveries from its channel.
        return await AwaitAsync(destination, predicate, timeout, cancellationToken, () => new Source(tap)).ConfigureAwait(false);
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

        foreach (var tap in taps)
        {
            try
            {
                if (tap.Channel.IsOpen)
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
    }

    private async Task<Tap> DeclareAsync(string destination, CancellationToken cancellationToken)
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
            // a second reader must be possible, because a silent SingleReader violation is what let a
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

        var tap = new Tap(queue, deliveries.Reader, channel);
        _taps[destination] = tap;
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

    private static ProtoMessage Convert(BasicDeliverEventArgs delivery)
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
        var destination = string.IsNullOrEmpty(delivery.Exchange) ? delivery.RoutingKey : delivery.Exchange;
        return new ProtoMessage(
            destination,
            Encoding.UTF8.GetString(delivery.Body.Span),
            headers,
            properties.ContentType);
    }

    /// <summary>
    /// One destination's tap: the queue that was declared for the test, its delivery channel and
    /// channel, and the log of every delivery the tap has read. The log is append-only and positions
    /// are the log indices; a delivery that matched an await is consumed by the shared queue, so an
    /// unmatched one stays for a later await.
    /// </summary>
    private sealed class Tap(string queue, ChannelReader<ProtoMessage> deliveries, IChannel channel)
    {
        public string Queue { get; } = queue;

        public ChannelReader<ProtoMessage> Deliveries { get; } = deliveries;

        public IChannel Channel { get; } = channel;

        public List<ProtoMessage> Log { get; } = [];

        public void Append(ProtoMessage message) => Log.Add(message);
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
