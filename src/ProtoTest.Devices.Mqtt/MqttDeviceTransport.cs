namespace ProtoTest.Devices.Mqtt;

using System.Buffers;
using System.Threading.Channels;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using ProtoTest.Core;

/// <summary>
/// Talks to devices over MQTT: the device's address names the broker, the endpoint settings (or the
/// address query, for a hand-registered endpoint) name the publish and subscribe topics, and the
/// transport keeps one broker connection per device instance. A send publishes to the device's publish
/// topic, a receive yields the next message delivered to its subscribe filter; the broker is the
/// simulator in CI or the real one behind the lab.
/// </summary>
public sealed class MqttDeviceTransport : IProtoDeviceTransport
{
    /// <summary>The transport's name, as registrations and configuration refer to it.</summary>
    public const string TransportName = "Mqtt";

    private readonly MqttDeviceOptions _options;

    /// <summary>Creates the transport with the run's MQTT options.</summary>
    public MqttDeviceTransport(MqttDeviceOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public string Name => TransportName;

    /// <inheritdoc />
    public ValueTask<IProtoDeviceConnection> ConnectAsync(
        DeviceEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return ConnectAsync(
            ProtoHost.CurrentContextOrNull ?? throw new InvalidOperationException(
                $"Connecting a device over MQTT needs a running test; '{endpoint.DeviceId}' was reached outside one."),
            endpoint,
            cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<IProtoDeviceConnection> ConnectAsync(
        ProtoExecutionContext context,
        DeviceEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(endpoint);
        var target = MqttDeviceAddress.Parse(endpoint.Address, endpoint.Settings);

        // The client id carries the test and the device id, so a rerun or a parallel test cannot take
        // over another conversation's broker session.
        var client = new MqttClientFactory().CreateMqttClient();
        var received = Channel.CreateUnbounded<DeviceFrame>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

        client.ApplicationMessageReceivedAsync += args =>
        {
            received.Writer.TryWrite(ToFrame(args.ApplicationMessage));
            return Task.CompletedTask;
        };
        client.DisconnectedAsync += _ =>
        {
            // A closed connection ends a pending receive like a closed socket, so the device reports
            // it instead of waiting for a frame that can never arrive.
            received.Writer.TryComplete();
            return Task.CompletedTask;
        };

        var clientOptions = BuildClientOptions(target, context.TestId, endpoint.DeviceId, _options);
        try
        {
            await ProtoDeviceConnect
                .WithTimeoutAsync(
                    target.Broker,
                    _options.ConnectTimeout,
                    token => ConnectAndSubscribeAsync(client, clientOptions, target, token),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            received.Writer.TryComplete();
            client.Dispose();
            throw;
        }

        return new MqttDeviceConnection(client, target, received.Reader);
    }

    private static MqttClientOptions BuildClientOptions(
        MqttEndpointTarget target,
        string testId,
        string deviceId,
        MqttDeviceOptions options)
    {
        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(target.Host, target.Port)
            .WithClientId($"prototest-{testId}-{deviceId}")
            .WithProtocolVersion(MqttProtocolVersion.V500)
            .WithCleanSession(true);
        if (options.KeepAlivePeriod is { } keepAlive)
        {
            builder.WithKeepAlivePeriod(keepAlive);
        }

        if (options.MaxPacketBytes > 0)
        {
            builder.WithMaximumPacketSize((uint)options.MaxPacketBytes);
        }

        return builder.Build();
    }

    private static async Task<IMqttClient> ConnectAndSubscribeAsync(
        IMqttClient client,
        MqttClientOptions clientOptions,
        MqttEndpointTarget target,
        CancellationToken cancellationToken)
    {
        var connect = await client.ConnectAsync(clientOptions, cancellationToken).ConfigureAwait(false);
        if (connect.ResultCode != MqttClientConnectResultCode.Success)
        {
            throw new InvalidOperationException(
                $"The MQTT broker at '{target.Broker}' refused the connection: {connect.ResultCode}.");
        }

        MqttClientSubscribeResult subscription;
        try
        {
            subscription = await client
                .SubscribeAsync(
                    new MqttClientSubscribeOptionsBuilder()
                        .WithTopicFilter(target.SubscribeTopic, MqttQualityOfServiceLevel.AtLeastOnce)
                        .Build(),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A broker that rejects the filter outright (a protocol violation) fails the connect with
            // the topic named, like a SUBACK failure below.
            throw new InvalidOperationException(
                $"Subscribing to '{target.SubscribeTopic}' on the MQTT broker at '{target.Broker}' failed.",
                exception);
        }

        var item = subscription.Items.FirstOrDefault(candidate =>
                string.Equals(candidate.TopicFilter.Topic, target.SubscribeTopic, StringComparison.Ordinal))
            ?? subscription.Items.FirstOrDefault();
        if (item is null || item.ResultCode is not (
                MqttClientSubscribeResultCode.GrantedQoS0 or
                MqttClientSubscribeResultCode.GrantedQoS1 or
                MqttClientSubscribeResultCode.GrantedQoS2))
        {
            throw new InvalidOperationException(
                $"The MQTT broker at '{target.Broker}' refused the subscription to " +
                $"'{target.SubscribeTopic}': {item?.ResultCode.ToString() ?? "no result"}.");
        }

        return client;
    }

    private static DeviceFrame ToFrame(MqttApplicationMessage message)
    {
        var payload = message.Payload.ToArray();
        var mediaType = !string.IsNullOrWhiteSpace(message.ContentType)
            ? message.ContentType
            : message.PayloadFormatIndicator == MqttPayloadFormatIndicator.CharacterData
                ? "text/plain"
                : "application/octet-stream";
        return new DeviceFrame(payload, mediaType);
    }
}

/// <summary>
/// One open broker connection: sends publish to the device's publish topic, receives yield the next
/// message the broker delivered to its subscribe filter, and a broker-side disconnect ends a pending
/// receive instead of waiting forever.
/// </summary>
internal sealed class MqttDeviceConnection(
    IMqttClient client,
    MqttEndpointTarget target,
    ChannelReader<DeviceFrame> received) : IProtoDeviceConnection
{
    /// <summary>How long the release waits for the broker to acknowledge a clean disconnect.</summary>
    private static readonly TimeSpan DisconnectBound = TimeSpan.FromSeconds(2);

    public string? RemoteAddress => target.Broker;

    public async ValueTask SendAsync(DeviceFrame frame, CancellationToken cancellationToken = default)
    {
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(target.PublishTopic)
            .WithPayload(frame.Payload.ToArray())
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithContentType(frame.MediaType)
            .WithPayloadFormatIndicator(
                frame.MediaType.StartsWith("text", StringComparison.OrdinalIgnoreCase)
                    ? MqttPayloadFormatIndicator.CharacterData
                    : MqttPayloadFormatIndicator.Unspecified)
            .Build();
        var result = await PublishAsync(message, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Publishing to '{target.PublishTopic}' on the MQTT broker at '{target.Broker}' failed: {result.ReasonCode}.");
        }
    }

    private async Task<MqttClientPublishResult> PublishAsync(
        MqttApplicationMessage message,
        CancellationToken cancellationToken)
    {
        try
        {
            return await client.PublishAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A broker that dropped the connection fails the publish naming the topic and broker, not
            // as a raw client exception.
            throw new InvalidOperationException(
                $"Publishing to '{target.PublishTopic}' on the MQTT broker at '{target.Broker}' failed.",
                exception);
        }
    }

    public async ValueTask<DeviceFrame?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await received.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            // The connection ended; the device reports the close instead of waiting for a frame.
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (client.IsConnected)
            {
                using var bound = new CancellationTokenSource(DisconnectBound);
                await client
                    .DisconnectAsync(
                        new MqttClientDisconnectOptionsBuilder()
                            .WithReason(MqttClientDisconnectOptionsReason.NormalDisconnection)
                            .Build(),
                        bound.Token)
                    .ConfigureAwait(false);
            }
        }
        catch
        {
            // Disconnecting is best-effort; the client is disposed either way.
        }
        finally
        {
            client.Dispose();
        }
    }
}
