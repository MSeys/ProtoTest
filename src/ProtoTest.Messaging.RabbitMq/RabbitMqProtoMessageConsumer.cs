namespace ProtoTest.Messaging.RabbitMq;

using System.Text;
using global::RabbitMQ.Client;
using global::RabbitMQ.Client.Events;
using global::RabbitMQ.Client.Exceptions;
using ProtoTest.Messaging;

/// <summary>
/// One test's RabbitMQ consumer: its own channel on the run's shared connection, every channel operation
/// serialized under its own lock, and an exclusive, auto-delete tap queue per destination. Prepared
/// queues are declared before the act; a destination that was never prepared is declared just in time at
/// the first await, which only sees messages published after the await begins. All queues are deleted
/// when the consumer is disposed with the test, and an exclusive queue never competes with the
/// application's own consumers.
/// </summary>
internal sealed class RabbitMqProtoMessageConsumer : IProtoMessageConsumer
{
    private readonly RabbitMqMessageBroker _broker;
    private readonly ProtoLock _gate = new();
    private readonly Dictionary<string, string> _queues = new(StringComparer.Ordinal);
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
                if (!string.IsNullOrWhiteSpace(destination) && !_queues.ContainsKey(destination))
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
        string queue;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_queues.TryGetValue(destination, out queue!))
            {
                queue = Declare(destination);
            }
        }

        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            BasicGetResult? result;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                result = _channel!.BasicGet(queue, autoAck: true);
            }

            if (result is not null)
            {
                var message = Convert(result);
                if (predicate(message))
                {
                    return message;
                }
            }

            // The deadline is checked on every iteration, including after a discarded message: a stream
            // of non-matching traffic must not keep an await spinning past its timeout.
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    $"No message matching the predicate arrived on '{destination}' within {timeout.TotalSeconds:0.###}s.");
            }

            await Task.Delay(_broker.PollInterval, cancellationToken);
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
                foreach (var queue in _queues.Values)
                {
                    try
                    {
                        channel.QueueDelete(queue, ifUnused: false, ifEmpty: false);
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

    private IModel Channel()
        => _channel is { IsOpen: true } ? _channel : _channel = _broker.CreateChannel();

    private string Declare(string destination)
    {
        var channel = Channel();
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

        _queues[destination] = queue;
        return queue;
    }

    private static ProtoMessage Convert(BasicGetResult result)
    {
        var headers = result.BasicProperties.Headers is null
            ? null
            : result.BasicProperties.Headers
                .ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value switch
                    {
                        byte[] bytes => Encoding.UTF8.GetString(bytes),
                        null => null,
                        var value => value.ToString()
                    },
                    StringComparer.OrdinalIgnoreCase);
        var destination = string.IsNullOrEmpty(result.Exchange) ? result.RoutingKey : result.Exchange;
        return new ProtoMessage(
            destination,
            Encoding.UTF8.GetString(result.Body.ToArray()),
            headers,
            result.BasicProperties.ContentType);
    }
}
