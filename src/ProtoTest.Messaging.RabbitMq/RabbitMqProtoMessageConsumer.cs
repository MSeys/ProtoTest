namespace ProtoTest.Messaging.RabbitMq;

using System.Text;
using System.Threading.Channels;
using global::RabbitMQ.Client;
using global::RabbitMQ.Client.Events;
using global::RabbitMQ.Client.Exceptions;
using ProtoTest.Messaging;

/// <summary>
/// One test's RabbitMQ consumer: its own channel on the run's shared connection, an exclusive,
/// auto-delete tap queue per destination, and an asynchronous consumer per queue feeding an unbounded
/// channel, so an await reacts to a delivery instead of polling. Prepared queues are declared before
/// the act; a destination that was never prepared is declared just in time at the first await, which
/// only sees messages published after the await begins. All queues are deleted when the consumer is
/// disposed with the test, and an exclusive queue never competes with the application's own consumers.
/// </summary>
internal sealed class RabbitMqProtoMessageConsumer : IProtoMessageConsumer
{
    private readonly RabbitMqMessageBroker _broker;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, Tap> _taps = new(StringComparer.Ordinal);
    private IChannel? _channel;
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

        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
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
                throw Timeout(destination, timeout);
            }

            if (predicate(message))
            {
                return message;
            }

            // A non-matching delivery is discarded, like the auto-acking poll this replaced. The
            // deadline is re-checked before the next read, so a stream of non-matching traffic cannot
            // keep an await running past its timeout.
        }
    }

    public async ValueTask DisposeAsync()
    {
        IChannel? channel;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            channel = _channel;
            _channel = null;
        }
        finally
        {
            _gate.Release();
        }

        if (channel is null)
        {
            return;
        }

        try
        {
            if (channel.IsOpen)
            {
                foreach (var tap in _taps.Values)
                {
                    try
                    {
                        await channel.QueueDeleteAsync(tap.Queue, ifUnused: false, ifEmpty: false).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        // Cleanup only: a queue that is already gone must not fail the test's teardown.
                    }
                }
            }
        }
        finally
        {
            await channel.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static TimeoutException Timeout(string destination, TimeSpan timeout)
        => new($"No message matching the predicate arrived on '{destination}' within {timeout.TotalSeconds:0.###}s.");

    private async Task<IChannel> RabbitChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        if (_channel is not null)
        {
            await _channel.DisposeAsync().ConfigureAwait(false);
            _channel = null;
        }

        _channel = await _broker.CreateChannelAsync(cancellationToken).ConfigureAwait(false);
        return _channel;
    }

    private async Task<Tap> DeclareAsync(string destination, CancellationToken cancellationToken)
    {
        var channel = await RabbitChannelAsync(cancellationToken).ConfigureAwait(false);
        var queue = (await channel.QueueDeclareAsync(
            queue: $"prototest-{Guid.NewGuid():N}",
            durable: false,
            exclusive: true,
            autoDelete: true,
            arguments: null,
            cancellationToken: cancellationToken).ConfigureAwait(false)).QueueName;
        try
        {
            // Direct exchanges match the destination routing key; "#" keeps topic exchanges catch-all;
            // fanout and headers exchanges ignore the routing key and match these argument-less bindings.
            // RabbitMQ still delivers one copy per message even when both bindings match this queue.
            await channel.QueueBindAsync(queue, destination, routingKey: destination, arguments: null, cancellationToken: cancellationToken).ConfigureAwait(false);
            await channel.QueueBindAsync(queue, destination, routingKey: "#", arguments: null, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationInterruptedException exception) when (exception.ShutdownReason is { ReplyCode: 404 })
        {
            // The broker closes the channel on this error, so the caller must not keep using it.
            throw new InvalidOperationException(
                $"Cannot await messages on '{destination}': the exchange '{destination}' does not exist on the broker. " +
                "Declare the exchange before the test awaits it.",
                exception);
        }

        var deliveries = Channel.CreateUnbounded<ProtoMessage>(new UnboundedChannelOptions
        {
            SingleReader = true,
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
        await channel.BasicConsumeAsync(
            queue,
            autoAck: true,
            consumerTag: string.Empty,
            noLocal: false,
            exclusive: false,
            arguments: null,
            consumer: consumer,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var tap = new Tap(queue, deliveries.Reader);
        _taps[destination] = tap;
        return tap;
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

    private sealed record Tap(string Queue, ChannelReader<ProtoMessage> Deliveries);
}
