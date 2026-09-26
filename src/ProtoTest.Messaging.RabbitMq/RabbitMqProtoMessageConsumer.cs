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
/// begins. Awaits on one consumer serialize in call order, and a delivery that matches no awaited
/// predicate is buffered and offered to a later await instead of being discarded, so concurrent awaits
/// neither lose nor steal each other's messages. All queues are deleted when the consumer is disposed
/// with the test, and an exclusive queue never competes with the application's own consumers.
/// </summary>
internal sealed class RabbitMqProtoMessageConsumer : IProtoMessageConsumer
{
    private readonly RabbitMqMessageBroker _broker;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _awaitGate = new(1, 1);
    private readonly Dictionary<string, Tap> _taps = new(StringComparer.Ordinal);
    private bool _disposed;

    public RabbitMqProtoMessageConsumer(RabbitMqMessageBroker broker)
    {
        ArgumentNullException.ThrowIfNull(broker);
        _broker = broker;
    }

    public async ValueTask PrepareAsync(
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

    public async ValueTask<ProtoMessage> AwaitAsync(
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

        // Awaits serialize on _awaitGate: the active one inspects the deliveries no earlier await
        // matched first, then reads new ones. A delivery that does not match stays in the tap's
        // buffer instead of being discarded, so the waiter it belongs to still finds it.
        await _awaitGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var deadline = DateTime.UtcNow + timeout;
            var scanned = 0;
            while (true)
            {
                while (scanned < tap.Buffered.Count)
                {
                    var candidate = tap.Buffered[scanned];
                    if (predicate(candidate))
                    {
                        tap.Buffered.RemoveAt(scanned);
                        return candidate;
                    }

                    scanned++;
                }

                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    throw Timeout(destination, timeout);
                }

                ProtoMessage message;
                using var expiry = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                expiry.CancelAfter(remaining);
                try
                {
                    message = await tap.Deliveries.ReadAsync(expiry.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // A delivery assigned at the same instant the deadline passes must win, never be
                    // dropped: inspect one already-consumed delivery before giving up on the timeout.
                    if (tap.Deliveries.TryRead(out var late))
                    {
                        if (predicate(late))
                        {
                            return late;
                        }

                        tap.Buffered.Add(late);
                    }

                    throw Timeout(destination, timeout);
                }

                tap.Buffered.Add(message);
            }
        }
        finally
        {
            _awaitGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
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

    private static TimeoutException Timeout(string destination, TimeSpan timeout)
        => new($"No message matching the predicate arrived on '{destination}' within {timeout.TotalSeconds:0.###}s.");

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
            // Awaits serialize on _awaitGate, but the channel stays honest without it: a second reader
            // must be possible, because a silent SingleReader violation is what let a racing await
            // consume the delivery another await owned.
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

    private sealed record Tap(string Queue, ChannelReader<ProtoMessage> Deliveries, IChannel Channel)
    {
        /// <summary>
        /// Deliveries read by an await whose predicate did not match. They are not consumed and a later
        /// await on the same consumer still inspects them, so a racing await cannot steal them.
        /// </summary>
        public List<ProtoMessage> Buffered { get; } = [];
    }
}
