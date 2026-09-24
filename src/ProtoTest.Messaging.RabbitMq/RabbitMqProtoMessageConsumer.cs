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
    private readonly ProtoLock _gate = new();
    private readonly Dictionary<string, Tap> _taps = new(StringComparer.Ordinal);
    private IModel? _channel;
    private bool _disposed;

    public RabbitMqProtoMessageConsumer(RabbitMqMessageBroker broker)
    {
        ArgumentNullException.ThrowIfNull(broker);
        _broker = broker;
    }

    public ValueTask PrepareAsync(
        IReadOnlyCollection<string> destinations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destinations);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            foreach (var destination in destinations)
            {
                if (!string.IsNullOrWhiteSpace(destination) && !_taps.ContainsKey(destination))
                {
                    Declare(destination);
                }
            }
        }

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
        Tap tap;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_taps.TryGetValue(destination, out tap!))
            {
                tap = Declare(destination);
            }
        }

        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                throw Timeout(destination, timeout);
            }

            BasicDeliverEventArgs delivery;
            using var expiry = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            expiry.CancelAfter(remaining);
            try
            {
                delivery = await tap.Deliveries.ReadAsync(expiry.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw Timeout(destination, timeout);
            }

            var message = Convert(delivery);
            if (predicate(message))
            {
                return message;
            }

            // A non-matching delivery is discarded, like the auto-acking poll this replaced. The
            // deadline is re-checked before the next read, so a stream of non-matching traffic cannot
            // keep an await running past its timeout.
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;
            var channel = _channel;
            _channel = null;
            if (channel is null)
            {
                return ValueTask.CompletedTask;
            }

            if (channel.IsOpen)
            {
                foreach (var tap in _taps.Values)
                {
                    try
                    {
                        channel.QueueDelete(tap.Queue, ifUnused: false, ifEmpty: false);
                    }
                    catch (Exception)
                    {
                        // Cleanup only: a queue that is already gone must not fail the test's teardown.
                    }
                }
            }

            channel.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private static TimeoutException Timeout(string destination, TimeSpan timeout)
        => new($"No message matching the predicate arrived on '{destination}' within {timeout.TotalSeconds:0.###}s.");

    private IModel RabbitChannel()
        => _channel is { IsOpen: true } ? _channel : _channel = _broker.CreateChannel();

    private Tap Declare(string destination)
    {
        var channel = RabbitChannel();
        var queue = channel.QueueDeclare(
            queue: $"prototest-{Guid.NewGuid():N}",
            durable: false,
            exclusive: true,
            autoDelete: true,
            arguments: null).QueueName;
        try
        {
            // Direct exchanges match the destination routing key; "#" keeps topic exchanges catch-all;
            // fanout and headers exchanges ignore the routing key and match these argument-less bindings.
            // RabbitMQ still delivers one copy per message even when both bindings match this queue.
            channel.QueueBind(queue, destination, routingKey: destination);
            channel.QueueBind(queue, destination, routingKey: "#");
        }
        catch (OperationInterruptedException exception) when (exception.ShutdownReason is { ReplyCode: 404 })
        {
            // The broker closes the channel on this error, so the caller must not keep using it.
            throw new InvalidOperationException(
                $"Cannot await messages on '{destination}': the exchange '{destination}' does not exist on the broker. " +
                "Declare the exchange before the test awaits it.",
                exception);
        }

        var deliveries = Channel.CreateUnbounded<BasicDeliverEventArgs>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.Received += (_, delivery) =>
        {
            deliveries.Writer.TryWrite(delivery);
            return Task.CompletedTask;
        };
        channel.BasicConsume(queue, autoAck: true, consumer);
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
            Encoding.UTF8.GetString(delivery.Body.ToArray()),
            headers,
            properties.ContentType);
    }

    private sealed record Tap(string Queue, ChannelReader<BasicDeliverEventArgs> Deliveries);
}
