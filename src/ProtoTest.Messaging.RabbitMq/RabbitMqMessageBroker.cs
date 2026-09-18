namespace ProtoTest.Messaging.RabbitMq;

using System.Text;
using global::RabbitMQ.Client;
using global::RabbitMQ.Client.Events;
using ProtoTest.Messaging;

/// <summary>
/// The RabbitMQ adapter. Publishing sends to the exchange named like the destination, so the app's
/// topology decides routing. Awaiting declares a fresh exclusive, auto-delete queue, binds it catch-all
/// to the destination exchange, drains it while waiting, and deletes it afterwards - a per-test tap
/// that never competes with the application's own consumers and never sees another test's messages.
/// </summary>
public sealed class RabbitMqMessageBroker : IProtoMessageBroker, IProtoMessageBrokerSetup, IDisposable
{
    private readonly ProtoRabbitMqOptions _options;
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _taps = new(StringComparer.Ordinal);
    private IConnection? _connection;
    private IModel? _channel;
    private long _received;

    public RabbitMqMessageBroker(ProtoRabbitMqOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public string Name => "RabbitMQ";

    public long Position => Interlocked.Read(ref _received);

    /// <summary>
    /// Declares one tap queue per awaited destination and drains any backlog from earlier tests. From
    /// here on, anything the application publishes is queued for this test, so the usual act-then-await
    /// order works - unlike binding at await time, which would miss it.
    /// </summary>
    public void Prepare(IReadOnlyCollection<string> destinations)
    {
        ArgumentNullException.ThrowIfNull(destinations);
        var channel = Channel();
        lock (_gate)
        {
            foreach (var destination in destinations)
            {
                if (string.IsNullOrWhiteSpace(destination))
                {
                    continue;
                }

                if (!_taps.TryGetValue(destination, out var queue))
                {
                    queue = DeclareTap(channel, destination);
                    _taps[destination] = queue;
                }

                // Start empty: a message this test did not cause must not satisfy its await.
                channel.QueuePurge(queue);
            }
        }
    }

    public ValueTask PublishAsync(ProtoMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var channel = Channel();
        var properties = channel.CreateBasicProperties();
        properties.ContentType = message.ContentType ?? "application/json";
        properties.Persistent = false;
        if (message.Headers is not null)
        {
            properties.Headers = message.Headers
                .Where(pair => pair.Value is not null)
                .ToDictionary(pair => pair.Key, pair => (object)pair.Value!);
        }

        channel.BasicPublish(
            exchange: message.Destination,
            routingKey: message.Destination,
            basicProperties: properties,
            body: Encoding.UTF8.GetBytes(message.Payload ?? string.Empty));
        return ValueTask.CompletedTask;
    }

    public async ValueTask<ProtoMessage> AwaitAsync(
        string destination,
        Func<ProtoMessage, bool> predicate,
        TimeSpan timeout,
        long afterPosition,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(predicate);
        var channel = Channel();
        string queue;
        var deleteAfter = false;
        lock (_gate)
        {
            if (_taps.TryGetValue(destination, out var prepared))
            {
                queue = prepared;
            }
            else
            {
                // Not declared up front: bind just in time, which only works for messages that come later.
                queue = DeclareTap(channel, destination);
                deleteAfter = true;
            }
        }

        try
        {
            var deadline = DateTime.UtcNow + timeout;
            while (true)
            {
                var result = channel.BasicGet(queue, autoAck: true);
                if (result is not null)
                {
                    var sequence = Interlocked.Increment(ref _received);
                    var message = Convert(result);
                    if (sequence > afterPosition && predicate(message))
                    {
                        return message;
                    }
                }
                else if (DateTime.UtcNow >= deadline)
                {
                    throw new TimeoutException(
                        $"No message matching the predicate arrived on '{destination}' within {timeout.TotalSeconds:0.###}s.");
                }
                else if (cancellationToken.WaitHandle.WaitOne(_options.PollInterval))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
        }
        finally
        {
            if (deleteAfter)
            {
                channel.QueueDelete(queue, ifUnused: false, ifEmpty: false);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _channel?.Dispose();
            _channel = null;
            _connection?.Dispose();
            _connection = null;
        }
    }

    private IModel Channel()
    {
        lock (_gate)
        {
            if (_channel is { IsOpen: true })
            {
                return _channel;
            }

            try
            {
                var factory = new ConnectionFactory
                {
                    Uri = new Uri(_options.ConnectionString),
                    DispatchConsumersAsync = false
                };
                _connection = factory.CreateConnection("ProtoTest.Messaging");
                _channel = _connection.CreateModel();
                return _channel;
            }
            catch (Exception exception) when (exception is UriFormatException
                or global::RabbitMQ.Client.Exceptions.BrokerUnreachableException)
            {
                throw new InvalidOperationException(
                    $"The RabbitMQ broker at '{_options.ConnectionString}' is unreachable. Configure " +
                    "'ProtoTest:Messaging:RabbitMq:ConnectionString' for this environment.",
                    exception);
            }
        }
    }

    private static string DeclareTap(IModel channel, string destination)
    {
        var queue = channel.QueueDeclare(
            queue: $"prototest-{Guid.NewGuid():N}",
            durable: false,
            exclusive: true,
            autoDelete: true,
            arguments: null);
        channel.QueueBind(queue.QueueName, destination, routingKey: "#");
        return queue.QueueName;
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
