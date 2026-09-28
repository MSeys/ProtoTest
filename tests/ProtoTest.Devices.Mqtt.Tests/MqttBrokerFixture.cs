namespace ProtoTest.Devices.Mqtt.Tests;

using System.Buffers;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using ProtoTest.Devices;
using ProtoTest.Devices.Mqtt.Testcontainers;

/// <summary>
/// The suite's one broker: a configured <c>ProtoTest:Devices:Mqtt:Broker</c> wins, otherwise a
/// Mosquitto container starts once and every parallel test shares it with its own topic namespace.
/// </summary>
internal static class MqttBrokerFixture
{
    private static readonly Lazy<(MosquittoBroker? Broker, string? Error)> Container = new(
        static () =>
        {
            var result = MosquittoBroker.TryStart();
            return (result.Resource, result.Error);
        },
        LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Returns the broker address, or skips the test when neither the environment nor a container provides one.</summary>
    public static string RequireBroker()
    {
        var configured = Environment.GetEnvironmentVariable("ProtoTest__Devices__Mqtt__Broker");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        if (Container.Value.Broker is { } broker)
        {
            return broker.ConnectionString;
        }

        Assert.Ignore(
            "No MQTT broker is available: set ProtoTest__Devices__Mqtt__Broker or start a container " +
            "runtime. " + Container.Value.Error);
        return null!;
    }

    /// <summary>Disposes the shared container, when this suite started one.</summary>
    public static async Task StopContainerAsync()
    {
        if (Container.IsValueCreated && Container.Value.Broker is { } broker)
        {
            await broker.DisposeAsync();
        }
    }

    /// <summary>A topic namespace unique to one test, so parallel tests share the broker safely.</summary>
    public static string TopicSpace() => $"prototest/{Guid.NewGuid():N}";

    /// <summary>A device simulator on the broker: subscribes to one topic and answers through a callback.</summary>
    internal sealed class Peer : IAsyncDisposable
    {
        private readonly IMqttClient _client;
        private readonly string _replyTopic;
        private readonly List<string> _received = [];
        private readonly ProtoLock _gate = new();

        private Peer(IMqttClient client, string replyTopic)
        {
            _client = client;
            _replyTopic = replyTopic;
        }

        /// <summary>The frames the peer received, as text or as a byte count.</summary>
        public IReadOnlyList<string> Received
        {
            get
            {
                lock (_gate)
                {
                    return [.. _received];
                }
            }
        }

        /// <summary>Answers one received frame, or returns null to stay silent.</summary>
        public Func<DeviceFrame, DeviceFrame?>? Responds { get; set; }

        public static async Task<Peer> ConnectAsync(string brokerAddress, string receiveTopic, string replyTopic)
        {
            var uri = new Uri(brokerAddress);
            var client = new MqttClientFactory().CreateMqttClient();
            var peer = new Peer(client, replyTopic);
            client.ApplicationMessageReceivedAsync += async args =>
            {
                var frame = ToFrame(args.ApplicationMessage);
                lock (peer._gate)
                {
                    peer._received.Add(frame.TryGetText(out var text) ? text : $"{frame.Payload.Length} bytes");
                }

                if (peer.Responds?.Invoke(frame) is { } reply)
                {
                    await client.PublishAsync(
                        new MqttApplicationMessageBuilder()
                            .WithTopic(peer._replyTopic)
                            .WithPayload(reply.Payload.ToArray())
                            .WithContentType(reply.MediaType)
                            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                            .Build());
                }
            };

            using var attempt = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await client.ConnectAsync(
                new MqttClientOptionsBuilder()
                    .WithTcpServer(uri.Host, uri.Port)
                    .WithClientId($"prototest-peer-{Guid.NewGuid():N}")
                    .WithProtocolVersion(MqttProtocolVersion.V500)
                    .WithCleanSession(true)
                    .Build(),
                attempt.Token);
            await client.SubscribeAsync(
                new MqttClientSubscribeOptionsBuilder()
                    .WithTopicFilter(receiveTopic, MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build(),
                attempt.Token);
            return peer;
        }

        public async ValueTask DisposeAsync()
        {
            if (_client.IsConnected)
            {
                using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try
                {
                    await _client.DisconnectAsync(
                        new MqttClientDisconnectOptionsBuilder()
                            .WithReason(MqttClientDisconnectOptionsReason.NormalDisconnection)
                            .Build(),
                        bound.Token);
                }
                catch
                {
                    // The test's teardown does not turn a best-effort disconnect into a failure.
                }
            }

            _client.Dispose();
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
}
