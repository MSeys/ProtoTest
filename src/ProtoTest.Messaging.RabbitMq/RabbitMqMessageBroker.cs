namespace ProtoTest.Messaging.RabbitMq;

using System.Text;
using global::RabbitMQ.Client;
using ProtoTest.Core;
using ProtoTest.Messaging;

/// <summary>
/// The RabbitMQ adapter. Publishing sends to the exchange named like the destination, so the app's
/// topology decides routing. Each test owns a consumer with its own channel and per-test tap queues,
/// deleted when the test ends, so parallel tests may share a destination without stealing each other's
/// messages. The connection is shared by the run and is thread-safe; channels are not, so each side
/// serializes its own.
/// </summary>
public sealed class RabbitMqMessageBroker : IProtoMessageBroker, IDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ProtoLock _gate = new();
    private IConnection? _connection;
    private IModel? _publishChannel;

    public RabbitMqMessageBroker(RabbitMqOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public string Name => "RabbitMQ";

    /// <summary>How often an await polls its tap queue.</summary>
    internal TimeSpan PollInterval => _options.PollInterval;

    /// <summary>Creates a consumer that owns its own channel on the shared connection.</summary>
    public ValueTask<IProtoMessageConsumer> CreateConsumerAsync(CancellationToken cancellationToken = default)
        => new(new RabbitMqProtoMessageConsumer(this));

    public ValueTask PublishAsync(ProtoMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var body = Encoding.UTF8.GetBytes(message.Payload ?? string.Empty);
        lock (_gate)
        {
            var channel = PublishChannel();
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
                body: body);
        }

        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _publishChannel?.Dispose();
            _publishChannel = null;
            _connection?.Dispose();
            _connection = null;
        }
    }

    /// <summary>Creates a channel on the shared connection; the caller owns and serializes it.</summary>
    internal IModel CreateChannel()
    {
        lock (_gate)
        {
            return Connection().CreateModel();
        }
    }

    private IModel PublishChannel()
    {
        if (_publishChannel is { IsOpen: true })
        {
            return _publishChannel;
        }

        _publishChannel?.Dispose();
        _publishChannel = Connection().CreateModel();
        return _publishChannel;
    }

    private IConnection Connection()
    {
        if (_connection is { IsOpen: true })
        {
            return _connection;
        }

        try
        {
            var factory = new ConnectionFactory
            {
                Uri = new Uri(_options.ConnectionString),
                DispatchConsumersAsync = false
            };
            _connection?.Dispose();
            _connection = factory.CreateConnection("ProtoTest.Messaging");
            return _connection;
        }
        catch (Exception exception) when (exception is UriFormatException
            or global::RabbitMQ.Client.Exceptions.BrokerUnreachableException)
        {
            throw new InvalidOperationException(
                $"The RabbitMQ broker at '{ProtoUriSanitizer.WithoutUserInfo(_options.ConnectionString)}' is unreachable. Configure " +
                "'ProtoTest:Messaging:RabbitMq:ConnectionString' for this environment.",
                exception);
        }
    }
}
