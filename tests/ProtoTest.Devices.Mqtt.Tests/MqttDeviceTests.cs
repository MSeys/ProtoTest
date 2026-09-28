namespace ProtoTest.Devices.Mqtt.Tests;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Devices.Exceptions;
using ProtoTest.Devices.Mqtt;
using ProtoTest.Devices.Mqtt.Testcontainers;
using ProtoTest.Testcontainers;

[TestFixture]
public sealed class MqttDeviceTests
{
    [OneTimeTearDown]
    public static async Task StopBroker() => await MqttBrokerFixture.StopContainerAsync();

    [Test]
    public async Task MqttClient_ShouldRoundTripTextFramesAndCoverTheAssertedKind()
    {
        var broker = MqttBrokerFixture.RequireBroker();
        var space = MqttBrokerFixture.TopicSpace();
        await using var peer = await MqttBrokerFixture.Peer.ConnectAsync(broker, $"{space}/S-001/out", $"{space}/S-001/in");
        peer.Responds = frame => frame.TryGetText(out var text) ? DeviceFrame.Text($"{text}_ACK") : null;

        var collector = new DeviceCoverageCollector("Sensors", [new PingProtocol()]);
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddDevices(devices => devices
            .AddMqttClient("Sensors", $"{space}/{{deviceId}}/out", $"{space}/{{deviceId}}/in", address: broker)
                .AddDevice<PingDevice>()
                .AddProtocol<PingProtocol>());
        builder.ConfigureServices(services => services.AddSingleton<IProtoCollector>(collector));
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("mqtt text round trip", "00001", TestMethods.Placeholder);

        var device = Proto.Context.Devices("Sensors").For<PingDevice>("S-001");
        var ack = await device.PingAsync();

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        var entity = host.Trace.Snapshot().Tests.Single().Entities!
            .Single(candidate => candidate.Kind == ProtoTraceEntityKinds.Device);
        var coverage = collector.GetReportItems().ToDictionary(item => item.Identifier, StringComparer.OrdinalIgnoreCase);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ack, Is.EqualTo("PING_ACK"), "the {deviceId} topics the registration set resolved per device");
            Assert.That(peer.Received, Does.Contain("PING"));
            Assert.That(entity.State["device.transport"], Is.EqualTo("Mqtt"));
            Assert.That(entity.State["device.address"], Is.EqualTo(broker), "the address is the broker; the topics are settings");
            Assert.That(
                entity.State["device.connected"],
                Is.EqualTo("false"),
                "the release path disconnects the device and finalises its state");
            Assert.That(coverage["PING_ACK"].IsCovered, Is.True, "the asserted kind is covered");
            Assert.That(coverage["PONG_ACK"].IsCovered, Is.False, "a kind no test asserted stays a gap");
        }
    }

    [Test]
    public async Task MqttClient_ShouldRoundTripBinaryFrames()
    {
        var broker = MqttBrokerFixture.RequireBroker();
        var space = MqttBrokerFixture.TopicSpace();
        await using var peer = await MqttBrokerFixture.Peer.ConnectAsync(broker, $"{space}/out", $"{space}/in");
        peer.Responds = frame => DeviceFrame.Binary(frame.Payload);

        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddDevices(devices => devices
            .AddMqttClient("Sensors", $"{space}/out", $"{space}/in", address: broker)
                .AddDevice<PingDevice>());
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("mqtt binary round trip", "00001", TestMethods.Placeholder);

        var device = Proto.Context.Devices("Sensors").For<PingDevice>("S-002");
        var payload = new byte[] { 0x01, 0x02, 0xFE, 0xFF };
        var echoed = await device.EchoAsync(payload);

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.That(echoed, Is.EqualTo(payload));
    }

    [Test]
    public async Task TwoMqttClients_ShouldExchangeFramesThroughTheBrokerWithTheirOwnTopics()
    {
        var broker = MqttBrokerFixture.RequireBroker();
        var space = MqttBrokerFixture.TopicSpace();
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddDevices(devices =>
        {
            devices
                .AddMqttClient("Callers", $"{space}/call", $"{space}/answer", address: broker)
                    .AddDevice<CallerDevice>();
            devices
                .AddMqttClient("Responders", $"{space}/answer", $"{space}/call", address: broker)
                    .AddDevice<ResponderDevice>();
        });
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("mqtt two clients", "00001", TestMethods.Placeholder);

        var caller = Proto.Context.Devices("Callers").For<CallerDevice>("C-001");
        var responder = Proto.Context.Devices("Responders").For<ResponderDevice>("R-001");
        // The responder subscribes before the caller publishes, so the ping is delivered to it.
        await responder.InitializeAsync();
        var answering = responder.AnswerAsync();
        var ack = await caller.PingAsync();
        await answering;

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        var entities = host.Trace.Snapshot().Tests.Single().Entities!
            .Where(candidate => candidate.Kind == ProtoTraceEntityKinds.Device)
            .ToDictionary(candidate => candidate.State["device.client"] ?? string.Empty, StringComparer.Ordinal);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ack, Is.EqualTo("PING_ACK"));
            Assert.That(entities, Has.Count.EqualTo(2), "each client owns its own device and connection");
            Assert.That(
                entities.Values.All(entity => entity.State["device.address"] == broker),
                Is.True,
                "both clients resolved the same broker; the topics are per-client settings");
        }
    }

    [Test]
    public async Task MqttClient_ShouldUseTheBrokerAContainerPublishedForTheRun()
    {
        var result = MosquittoBroker.TryStart();
        if (result.Resource is null)
        {
            Assert.Ignore($"No container runtime is available: {result.Error}");
        }

        await using var broker = result.Resource!;
        var space = MqttBrokerFixture.TopicSpace();
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddInfrastructure(
            "Mqtt",
            chain => chain.UseConfigured().UseContainer(broker),
            MqttDeviceOptions.BrokerSetting);
        builder.AddDevices(devices => devices
            .AddMqttClient("Sensors", $"{space}/out", $"{space}/in")
                .AddDevice<PingDevice>());
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("mqtt published broker", "00001", TestMethods.Placeholder);

        // No address was registered: the started container published the broker key for the run.
        var device = Proto.Context.Devices("Sensors").For<PingDevice>("S-003");
        await device.InitializeAsync();
        // Read while the run owns the container: releasing it clears the resolved connection string.
        var published = broker.ConnectionString;

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        var entity = host.Trace.Snapshot().Tests.Single().Entities!
            .Single(candidate => candidate.Kind == ProtoTraceEntityKinds.Device);
        Assert.That(
            entity.State["device.address"],
            Is.EqualTo(published),
            "the client resolved the broker the run started, not a registration default");
    }

    [Test]
    public async Task MqttClient_WhenTheAddressNamesTheTopics_ShouldUseTheAddressFallback()
    {
        var broker = MqttBrokerFixture.RequireBroker();
        var space = MqttBrokerFixture.TopicSpace();
        await using var peer = await MqttBrokerFixture.Peer.ConnectAsync(broker, $"{space}/address/out", $"{space}/address/in");
        peer.Responds = frame => frame.TryGetText(out var text) ? DeviceFrame.Text($"{text}_ACK") : null;

        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddDevices(devices => devices
            // A hand-registered endpoint carries the topics in the address query, without settings.
            .AddClient(
                "Sensors",
                new MqttDeviceTransport(new MqttDeviceOptions()),
                resolveAddress: (_, _) =>
                    $"{broker}?publishTopic={Uri.EscapeDataString($"{space}/address/out")}" +
                    $"&subscribeTopic={Uri.EscapeDataString($"{space}/address/in")}")
                .AddDevice<PingDevice>());
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("mqtt address topics", "00001", TestMethods.Placeholder);

        var device = Proto.Context.Devices("Sensors").For<PingDevice>("S-010");
        var ack = await device.PingAsync();

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.That(ack, Is.EqualTo("PING_ACK"), "the address query parameters are the fallback when no setting names the topics");
    }

    [Test]
    public async Task MqttClient_WhenSettingsAndTheAddressBothNameTopics_ShouldPreferTheSettings()
    {
        var broker = MqttBrokerFixture.RequireBroker();
        var space = MqttBrokerFixture.TopicSpace();
        // The peer serves only the settings topics: a transport that preferred the address parameters
        // would never reach it.
        await using var peer = await MqttBrokerFixture.Peer.ConnectAsync(broker, $"{space}/settings/out", $"{space}/settings/in");
        peer.Responds = frame => frame.TryGetText(out var text) ? DeviceFrame.Text($"{text}_ACK") : null;

        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddDevices(devices => devices
            .AddClient(
                "Sensors",
                new MqttDeviceTransport(new MqttDeviceOptions()),
                resolveAddress: (_, _) =>
                    $"{broker}?publishTopic={Uri.EscapeDataString($"{space}/address/out")}" +
                    $"&subscribeTopic={Uri.EscapeDataString($"{space}/address/in")}")
                .WithSetting("publishTopic", $"{space}/settings/out")
                .WithSetting("subscribeTopic", $"{space}/settings/in")
                .AddDevice<PingDevice>());
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("mqtt topic precedence", "00001", TestMethods.Placeholder);

        var device = Proto.Context.Devices("Sensors").For<PingDevice>("S-011");
        var ack = await device.PingAsync();

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.That(ack, Is.EqualTo("PING_ACK"), "a registration setting wins over the address parameter");
    }

    [Test]
    public async Task MqttClient_WhenTheBrokerIsDown_ShouldFailNamingTheAddress()
    {
        var port = TestNetworking.FreePort();
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddDevices(devices => devices
            .AddMqttClient("Sensors", "sensors/{deviceId}/out", "sensors/{deviceId}/in", address: $"mqtt://127.0.0.1:{port}")
                .AddDevice<PingDevice>());
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("mqtt broker down", "00001", TestMethods.Placeholder);

        var device = Proto.Context.Devices("Sensors").For<PingDevice>("S-004");
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await device.InitializeAsync());

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        await host.StopAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain($"127.0.0.1:{port}"));
            Assert.That(exception.Message, Does.Contain("could not connect"));
            Assert.That(exception.Message, Does.Contain("Mqtt"));
        }
    }

    [Test]
    public async Task MqttClient_WhenTheConnectOutlivesItsTimeout_ShouldFailNamed()
    {
        // A listener that accepts the TCP connection but never answers the MQTT handshake.
        await using var listener = SingleConnectionListener.Start();
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Devices:Mqtt:ConnectTimeout"] = "00:00:01"
            }));
        builder.AddDevices(devices => devices
            .AddMqttClient(
                "Sensors",
                "sensors/{deviceId}/out",
                "sensors/{deviceId}/in",
                address: $"mqtt://127.0.0.1:{listener.Port}")
                .AddDevice<PingDevice>());
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("mqtt connect timeout", "00001", TestMethods.Placeholder);

        var device = Proto.Context.Devices("Sensors").For<PingDevice>("S-005");
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await device.InitializeAsync());

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        await host.StopAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain("timed out"));
            Assert.That(exception.Message, Does.Contain($"127.0.0.1:{listener.Port}"));
        }
    }

    [Test]
    public async Task MqttClient_WhenTheExpectationTimesOut_ShouldFailWithTheFramesSeen()
    {
        var broker = MqttBrokerFixture.RequireBroker();
        var space = MqttBrokerFixture.TopicSpace();
        using var trace = new TemporaryTrace("mqtt-expectation");
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.OutputPath = trace.Path);
        builder.AddDevices(devices => devices
            .AddMqttClient("Sensors", $"{space}/out", $"{space}/in", address: broker)
                .AddDevice<PingDevice>());
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("mqtt expectation timeout", "00001", TestMethods.Placeholder);

        var device = Proto.Context.Devices("Sensors").For<PingDevice>("S-006");
        await device.SendText("PING");
        var exception = Assert.ThrowsAsync<DeviceAssertionException>(async () =>
            await device.AwaitAsync("never", TimeSpan.FromMilliseconds(250)));

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        await host.StopAsync();

        var command = host.Trace.Snapshot().Tests.Single().Entries.Single(entry => entry.Kind == "device.command");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain("the peer sends never"));
            Assert.That(exception.Message, Does.Contain("Frames exchanged"));
            Assert.That(exception.Message, Does.Contain("→ PING"));
            Assert.That(command.Outcome, Is.EqualTo(ProtoTraceOutcome.Failed));
        }
    }

    [Test]
    public async Task MqttClient_WhenTheExpectationIsCancelled_ShouldPropagateTheCancellation()
    {
        var broker = MqttBrokerFixture.RequireBroker();
        var space = MqttBrokerFixture.TopicSpace();
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddDevices(devices => devices
            .AddMqttClient("Sensors", $"{space}/out", $"{space}/in", address: broker)
                .AddDevice<PingDevice>());
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("mqtt expectation canceled", "00001", TestMethods.Placeholder);

        var device = Proto.Context.Devices("Sensors").For<PingDevice>("S-007");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        var canceled = Assert.CatchAsync<OperationCanceledException>(async () =>
            await device.AwaitAsync("never", TimeSpan.FromSeconds(15), cancellation.Token));

        await host.CompleteTestAsync(ProtoTestResult.Failed(canceled!));
        await host.StopAsync();

        Assert.That(canceled, Is.Not.Null, "the caller's cancellation wins over the expectation timeout");
    }

    [Test]
    public async Task MqttClient_WhenTheBrokerRefusesTheSubscribeFilter_ShouldFailNamingTheTopic()
    {
        var broker = MqttBrokerFixture.RequireBroker();
        var space = MqttBrokerFixture.TopicSpace();
        var filter = $"{space}/#/invalid";
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddDevices(devices => devices
            .AddMqttClient("Sensors", $"{space}/out", filter, address: broker)
                .AddDevice<PingDevice>());
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("mqtt bad topic", "00001", TestMethods.Placeholder);

        var device = Proto.Context.Devices("Sensors").For<PingDevice>("S-008");
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await device.InitializeAsync());

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        await host.StopAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain(filter), "the failure names the refused filter");
            Assert.That(exception.Message, Does.Contain("could not connect"), "the connect is what failed");
        }
    }
}
