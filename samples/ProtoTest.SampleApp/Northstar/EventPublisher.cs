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

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    /// <summary>Declares the application's event topology so operators and consumers find it ready.</summary>
    public async Task EnsureTopologyAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var exchange in Exchanges)
            {
                _ = await ChannelLockedAsync(exchange, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask PublishAsync(string destination, string payload, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var channel = await ChannelLockedAsync(destination, cancellationToken).ConfigureAwait(false);
            var properties = new BasicProperties
            {
                ContentType = "application/json",
                Persistent = false
            };
            await channel.BasicPublishAsync(
                exchange: destination,
                routingKey: destination,
                mandatory: false,
                basicProperties: properties,
                body: Encoding.UTF8.GetBytes(payload),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_channel is not null)
            {
                await _channel.DisposeAsync().ConfigureAwait(false);
                _channel = null;
            }

            if (_connection is not null)
            {
                await _connection.DisposeAsync().ConfigureAwait(false);
                _connection = null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Returns the open channel; the caller holds the gate across its use of the channel.</summary>
    private async Task<IChannel> ChannelLockedAsync(string exchange, CancellationToken cancellationToken)
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

        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }

        var factory = new ConnectionFactory { Uri = new Uri(connectionString) };
        _connection = await factory.CreateConnectionAsync("Northstar", cancellationToken).ConfigureAwait(false);
        _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        // The event exchange is part of the application's contract; a deployment may pre-declare it.
        await _channel.ExchangeDeclareAsync(
            exchange, ExchangeType.Fanout, durable: true, autoDelete: false, cancellationToken: cancellationToken).ConfigureAwait(false);
        return _channel;
    }
}
