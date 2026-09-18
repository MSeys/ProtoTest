namespace ProtoTest.SampleApp.Northstar;

using System.Text;
using global::RabbitMQ.Client;

/// <summary>
/// The application's side of messaging: it publishes domain events to the configured broker, or does
/// nothing when no broker is configured. The application knows nothing about ProtoTest.
/// </summary>
internal interface IEventPublisher
{
    ValueTask PublishAsync(string destination, string payload, CancellationToken cancellationToken = default);
}

internal sealed class NullEventPublisher : IEventPublisher
{
    public static NullEventPublisher Instance { get; } = new();

    public ValueTask PublishAsync(string destination, string payload, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;
}

internal sealed class RabbitMqEventPublisher(string connectionString) : IEventPublisher, IAsyncDisposable
{
    /// <summary>The exchanges this application owns; topology is declared at startup like a migration.</summary>
    private static readonly string[] Exchanges = ["invoice.paid"];

    private readonly object _gate = new();
    private IConnection? _connection;
    private IModel? _channel;

    /// <summary>Declares the application's event topology so operators and consumers find it ready.</summary>
    public void EnsureTopology()
    {
        foreach (var exchange in Exchanges)
        {
            _ = Channel(exchange);
        }
    }

    public ValueTask PublishAsync(string destination, string payload, CancellationToken cancellationToken = default)
    {
        var channel = Channel(destination);
        var properties = channel.CreateBasicProperties();
        properties.ContentType = "application/json";
        properties.Persistent = false;
        channel.BasicPublish(
            exchange: destination,
            routingKey: destination,
            basicProperties: properties,
            body: Encoding.UTF8.GetBytes(payload));
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _channel?.Dispose();
            _channel = null;
            _connection?.Dispose();
            _connection = null;
        }

        return ValueTask.CompletedTask;
    }

    private IModel Channel(string exchange)
    {
        lock (_gate)
        {
            if (_channel is { IsOpen: true })
            {
                return _channel;
            }

            var factory = new ConnectionFactory { Uri = new Uri(connectionString) };
            _connection = factory.CreateConnection("Northstar");
            _channel = _connection.CreateModel();
            // The event exchange is part of the application's contract; a deployment may pre-declare it.
            _channel.ExchangeDeclare(exchange, ExchangeType.Fanout, durable: true, autoDelete: false);
            return _channel;
        }
    }
}
