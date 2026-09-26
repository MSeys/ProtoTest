namespace ProtoTest.Messaging.RabbitMq;

using System.Text;
using global::RabbitMQ.Client;
using global::RabbitMQ.Client.Exceptions;
using ProtoTest.Core;
using ProtoTest.Messaging;

/// <summary>
/// The RabbitMQ adapter. Publishing sends to the exchange named like the destination, so the app's
/// topology decides routing. Each test owns a consumer with its own channel and per-test tap queues,
/// deleted when the test ends, so parallel tests may share a destination without stealing each other's
/// messages. The connection is shared by the run and is thread-safe; channels are not, so each side
/// serializes its own. Every broker call is asynchronous: the client's 7.x API has no synchronous one.
/// </summary>
internal sealed class RabbitMqMessageBroker : IProtoMessageBroker, IAsyncDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<string> _declared = new(StringComparer.Ordinal);
    private IConnection? _connection;
    private IChannel? _publishChannel;
    private bool _disposed;

    public RabbitMqMessageBroker(RabbitMqOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public string Name => "RabbitMQ";

    /// <summary>Creates a consumer that owns its own channel on the shared connection.</summary>
    public ValueTask<IProtoMessageConsumer> CreateConsumerAsync(CancellationToken cancellationToken = default)
        => new(new RabbitMqProtoMessageConsumer(this));

    /// <summary>
    /// Declares each destination as a fanout, durable, non-auto-delete exchange - the shape the
    /// application declares for its event exchanges - so publishing to it and binding a test's tap
    /// queue both work. The run remembers what it declared, so a destination is sent to the broker
    /// once and a repeated declaration is a no-op; an exchange that already exists with this shape,
    /// declared by the application or an earlier test, is left as it is.
    /// </summary>
    public async ValueTask DeclareAsync(
        IReadOnlyCollection<string> destinations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destinations);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var pending = destinations
                .Where(destination => !string.IsNullOrWhiteSpace(destination) && !_declared.Contains(destination))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (pending.Length == 0)
            {
                return;
            }

            var connection = await ConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            foreach (var destination in pending)
            {
                try
                {
                    await channel.ExchangeDeclareAsync(
                        destination,
                        ExchangeType.Fanout,
                        durable: true,
                        autoDelete: false,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                catch (OperationInterruptedException exception)
                {
                    throw new InvalidOperationException(
                        $"Cannot declare the destination '{destination}': the broker refused the declaration " +
                        $"({exception.ShutdownReason?.ReplyText ?? exception.Message}). A destination the application " +
                        "declares with another type or durability cannot be declared again.",
                        exception);
                }

                _declared.Add(destination);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask PublishAsync(ProtoMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var body = Encoding.UTF8.GetBytes(message.Payload ?? string.Empty);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // A released broker stays released: without this, the reopen in ConnectionAsync would
            // revive the run's connection for a late publisher and leak it.
            ObjectDisposedException.ThrowIf(_disposed, this);
            var channel = await PublishChannelAsync(cancellationToken).ConfigureAwait(false);
            var properties = new BasicProperties
            {
                ContentType = message.ContentType ?? "application/json",
                Persistent = false
            };
            if (message.Headers is not null)
            {
                var headers = new Dictionary<string, object?>();
                foreach (var (key, value) in message.Headers)
                {
                    if (value is not null)
                    {
                        headers[key] = value;
                    }
                }

                properties.Headers = headers;
            }

            await channel.BasicPublishAsync(
                exchange: message.Destination,
                routingKey: message.Destination,
                mandatory: false,
                basicProperties: properties,
                body: body,
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
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_publishChannel is not null)
            {
                await _publishChannel.DisposeAsync().ConfigureAwait(false);
                _publishChannel = null;
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

    /// <summary>Creates a channel on the shared connection; the caller owns and serializes it.</summary>
    internal async Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var connection = await ConnectionAsync(cancellationToken).ConfigureAwait(false);
            return await connection.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IChannel> PublishChannelAsync(CancellationToken cancellationToken)
    {
        if (_publishChannel is { IsOpen: true })
        {
            return _publishChannel;
        }

        if (_publishChannel is not null)
        {
            await _publishChannel.DisposeAsync().ConfigureAwait(false);
            _publishChannel = null;
        }

        var connection = await ConnectionAsync(cancellationToken).ConfigureAwait(false);
        _publishChannel = await connection.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return _publishChannel;
    }

    /// <summary>Returns the open shared connection, opening one when the broker dropped it.</summary>
    private async Task<IConnection> ConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true })
        {
            return _connection;
        }

        try
        {
            var factory = new ConnectionFactory
            {
                Uri = new Uri(_options.ConnectionString)
            };
            if (_connection is not null)
            {
                await _connection.DisposeAsync().ConfigureAwait(false);
                _connection = null;
            }

            _connection = await factory.CreateConnectionAsync("ProtoTest.Messaging", cancellationToken).ConfigureAwait(false);
            return _connection;
        }
        catch (Exception exception) when (exception is UriFormatException or BrokerUnreachableException)
        {
            throw new InvalidOperationException(
                $"The RabbitMQ broker at '{ProtoUriSanitizer.WithoutUserInfo(_options.ConnectionString)}' is unreachable. Configure " +
                "'ProtoTest:Messaging:RabbitMq:ConnectionString' for this environment.",
                exception);
        }
    }
}
