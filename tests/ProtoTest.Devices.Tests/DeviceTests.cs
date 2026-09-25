namespace ProtoTest.Devices.Tests;

using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Devices.Exceptions;

[TestFixture]
public sealed class DeviceTests
{
    [Test]
    public async Task Client_ShouldCreateOneDevicePerIdAndRecordItsExchange()
    {
        var output = TemporaryTracePath();
        try
        {
            var server = new FakeDeviceServer();
            var builder = new ProtoHostBuilder();
            builder.ConfigureTracing(options => options.OutputPath = output);
            builder.AddDevices(devices => devices
                .AddClient("Chargers", server.Transport, resolveAddress: ProtoDeviceAddress.Template("memory://cp-001"))
                    .AddDevice<FakeCharger>()
                    .AddProtocol<FakeProtocol>());
            await using var host = builder.Build();
            await host.StartAsync();

            await host.StartTestAsync("device first", "00001", TestMethods.Placeholder);
            var charger = Proto.Context.Devices("Chargers").For<FakeCharger>("CP-001");
            var again = Proto.Context.Devices("Chargers").For<FakeCharger>("CP-001");
            var ack = await charger.BootAsync();
            await host.CompleteTestAsync(ProtoTestResult.Passed);
            await host.StopAsync();

            var test = host.Trace.Snapshot().Tests.Single();
            var entity = test.Entities!.Single(candidate => candidate.Kind == ProtoTraceEntityKinds.Device);
            Assert.Multiple(() =>
            {
                Assert.That(again, Is.SameAs(charger), "one instance per (client, type, id) per test");
                Assert.That(ack, Is.EqualTo("BOOT_ACK"));
                Assert.That(entity.Id, Is.EqualTo("device:Chargers:FakeCharger:CP-001"));
                Assert.That(entity.State["device.client"], Is.EqualTo("Chargers"));
                Assert.That(entity.State["device.address"], Is.EqualTo("memory://cp-001"));
                Assert.That(
                    entity.State["device.connected"],
                    Is.EqualTo("false"),
                    "the release path disconnects the device and finalises its state");
                Assert.That(server.Sent, Is.EqualTo(new[] { "BOOT" }));
                Assert.That(
                    test.Entries.Select(entry => entry.Kind),
                    Does.Contain("device.send").And.Contain("device.receive").And.Contain("device.command")
                        .And.Contain("device.disconnect"));
            });
        }
        finally
        {
            if (File.Exists(output))
            {
                File.Delete(output);
            }
        }
    }

    [Test]
    public async Task Client_WhenDisconnectedExplicitly_ShouldReconnectAndRecordBothConnects()
    {
        var output = TemporaryTracePath();
        try
        {
            var server = new FakeDeviceServer();
            var builder = new ProtoHostBuilder();
            builder.ConfigureTracing(options => options.OutputPath = output);
            builder.AddDevices(devices => devices
                .AddClient("Chargers", server.Transport, resolveAddress: ProtoDeviceAddress.Template("memory://cp-001"))
                    .AddDevice<FakeCharger>());
            await using var host = builder.Build();
            await host.StartAsync();
            await host.StartTestAsync("disconnect and reconnect", "00001", TestMethods.Placeholder);

            var charger = Proto.Context.Devices("Chargers").For<FakeCharger>("CP-001");
            await charger.SendTextAsync("BOOT");
            await charger.CloseAsync();
            await charger.SendTextAsync("METER");

            await host.CompleteTestAsync(ProtoTestResult.Passed);
            await host.StopAsync();

            var test = host.Trace.Snapshot().Tests.Single();
            var entity = test.Entities!.Single(candidate => candidate.Kind == ProtoTraceEntityKinds.Device);
            // Identical error-free events collapse into one entry with a count, and the explicit
            // disconnect and the release one can sit under different parents, so sum the counts.
            var connects = test.Entries.Where(entry => entry.Kind == "device.connect").Sum(entry => entry.Count);
            var disconnects = test.Entries.Where(entry => entry.Kind == "device.disconnect").Sum(entry => entry.Count);
            Assert.Multiple(() =>
            {
                Assert.That(
                    connects,
                    Is.EqualTo(2),
                    "the explicit disconnect releases the connection, so the next send connects again");
                Assert.That(
                    disconnects,
                    Is.EqualTo(2),
                    "the explicit disconnect and the release path each record one disconnect");
                Assert.That(entity.State["device.connected"], Is.EqualTo("false"), "the final state is disconnected");
                Assert.That(server.Sent, Is.EqualTo(new[] { "BOOT", "METER" }));
            });
        }
        finally
        {
            if (File.Exists(output))
            {
                File.Delete(output);
            }
        }
    }

    [Test]
    public async Task Client_WhenNoInProcessTransportServesItsApplication_ShouldNameTheApplication()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddApplication("Api", app => app
            .AddDevices(devices => devices
                // A path-only client expects the application in-process; no transport serves it.
                .AddClient("Chargers", "InProcessWebSocket", path: "/ws/{deviceId}")
                    .AddDevice<FakeCharger>()));
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("missing in-process transport", "00001", TestMethods.Placeholder);

        var exception = Assert.Throws<InvalidOperationException>(
            () => Proto.Context.Devices("Chargers").For<FakeCharger>("CP-001"));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("'Api'"), "the failure names the application");
            Assert.That(exception.Message, Does.Contain("AddInProcessWebSocketDevices"));
            Assert.That(
                exception.Message,
                Does.Not.Contain("which is not registered"),
                "the client does not fall back to a generic missing-transport error");
        });
    }

    [Test]
    public async Task Client_ShouldCreateANewDeviceForTheNextTest()
    {
        var server = new FakeDeviceServer();
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddDevices(devices => devices
            .AddClient("Chargers", server.Transport, resolveAddress: ProtoDeviceAddress.Template("memory://cp-001"))
                .AddDevice<FakeCharger>());
        await using var host = builder.Build();
        await host.StartAsync();

        await host.StartTestAsync("first", "00001", TestMethods.Placeholder);
        var first = Proto.Context.Devices("Chargers").For<FakeCharger>("CP-001");
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        await host.StartTestAsync("second", "00002", TestMethods.Placeholder);
        var second = Proto.Context.Devices("Chargers").For<FakeCharger>("CP-001");
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.That(second, Is.Not.SameAs(first), "device instances do not leak between tests");
    }

    [Test]
    public async Task Client_ShouldUseTheResolverForEachDeviceId()
    {
        var output = TemporaryTracePath();
        try
        {
            var server = new FakeDeviceServer();
            var builder = new ProtoHostBuilder();
            builder.ConfigureTracing(options => options.OutputPath = output);
            builder.AddDevices(devices => devices
                .AddClient(
                    "Chargers",
                    server.Transport,
                    resolveAddress: (_, deviceId) => $"memory://resolved/{deviceId}")
                    .AddDevice<FakeCharger>());
            await using var host = builder.Build();
            await host.StartAsync();
            await host.StartTestAsync("resolver", "00001", TestMethods.Placeholder);

            var charger = Proto.Context.Devices("Chargers").For<FakeCharger>("CP-009");
            await charger.SendTextAsync("BOOT");

            await host.CompleteTestAsync(ProtoTestResult.Passed);
            await host.StopAsync();

            var entity = host.Trace.Snapshot().Tests.Single().Entities!
                .Single(candidate => candidate.Kind == ProtoTraceEntityKinds.Device);
            Assert.That(entity.State["device.address"], Is.EqualTo("memory://resolved/CP-009"));
        }
        finally
        {
            if (File.Exists(output))
            {
                File.Delete(output);
            }
        }
    }

    [Test]
    public async Task Client_ShouldResolveTheApplicationsAddress()
    {
        var server = new FakeDeviceServer();
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:Api:BaseUrl"] = "http://127.0.0.1:1"
            }));
        builder.AddDevices(devices => devices
            .AddClient("Chargers", server.Transport, resolveAddress: ProtoDeviceAddress.Template("unused"))
                .AddDevice<FakeCharger>());
        builder.AddApplication("Api", app => app.AddDevices(devices => devices
            .AddClient("AppChargers", server.Transport, resolveAddress: ProtoDeviceAddress.FromApplication("Api", "/ocpp/{deviceId}"))
                .AddDevice<FakeCharger>()));
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("application", "00001", TestMethods.Placeholder,
            [new ApplicationAttribute("Api")]);

        var charger = Proto.Context.Devices("AppChargers").For<FakeCharger>("CP-001");
        await charger.BootAsync();

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.That(
            server.Transport.LastEndpoint!.Address,
            Is.EqualTo("http://127.0.0.1:1/ocpp/CP-001"),
            "the client resolved the application's address with the device id");
    }

    [Test]
    public async Task Expect_WhenNeverObserved_ShouldFailWithDescriptionAndTheFramesSeen()
    {
        var output = TemporaryTracePath();
        try
        {
            var server = new FakeDeviceServer { Responds = false };
            var builder = new ProtoHostBuilder();
            builder.ConfigureTracing(options => options.OutputPath = output);
            builder.AddDevices(devices => devices
                .AddClient("Chargers", server.Transport, resolveAddress: ProtoDeviceAddress.Template("memory://cp-002"))
                    .AddDevice<FakeCharger>());
            await using var host = builder.Build();
            await host.StartAsync();
            await host.StartTestAsync("timeout", "00001", TestMethods.Placeholder);

            var charger = Proto.Context.Devices("Chargers").For<FakeCharger>("CP-002");
            await charger.SendTextAsync("BOOT");
            var exception = Assert.ThrowsAsync<DeviceAssertionException>(async () =>
                await charger.AwaitAsync("BOOT_ACK", TimeSpan.FromMilliseconds(150)));

            await host.CompleteTestAsync(ProtoTestResult.Failed(exception));
            await host.StopAsync();

            var test = host.Trace.Snapshot().Tests.Single();
            var command = test.Entries.Single(entry => entry.Kind == "device.command");
            Assert.Multiple(() =>
            {
                Assert.That(exception!.Message, Does.Contain("server sends BOOT_ACK"));
                Assert.That(exception.Message, Does.Contain("Frames exchanged"));
                Assert.That(exception.Message, Does.Contain("→ BOOT"));
                Assert.That(command.Outcome, Is.EqualTo(ProtoTraceOutcome.Failed));
            });
        }
        finally
        {
            if (File.Exists(output))
            {
                File.Delete(output);
            }
        }
    }

    [Test]
    public async Task Coverage_ShouldReportAssertedKindsAndTheProtocolsGaps()
    {
        var server = new FakeDeviceServer();
        var collector = new DeviceCoverageCollector("Chargers", [server.Protocol]);
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddDevices(devices => devices
            .AddClient("Chargers", server.Transport, resolveAddress: ProtoDeviceAddress.Template("memory://cp-003"))
                .AddDevice<FakeCharger>()
                .AddProtocol<FakeProtocol>());
        builder.ConfigureServices(services => services.AddSingleton<IProtoCollector>(collector));
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("coverage", "00001", TestMethods.Placeholder);

        var charger = Proto.Context.Devices("Chargers").For<FakeCharger>("CP-003");
        await charger.BootAsync();

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        var items = collector.GetReportItems().ToDictionary(item => item.Identifier, StringComparer.OrdinalIgnoreCase);
        Assert.Multiple(() =>
        {
            Assert.That(items["BOOT_ACK"].IsCovered, Is.True);
            Assert.That(items["METER_ACK"].IsCovered, Is.False, "a message kind no test asserted is a gap");
            Assert.That(items["METER_ACK"].Message, Does.Contain("never asserted"));
            Assert.That(collector.Category, Is.EqualTo("Device operations"));
        });
    }

    [Test]
    public async Task RequiresDevice_ShouldMatchTheRegisteredDeviceType()
    {
        var attribute = new RequiresDeviceAttribute<FakeCharger>();
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        await using var host = builder.Build();
        await host.StartAsync();

        var missing = attribute.GetSkipReason(host);

        var registered = new ProtoHostBuilder();
        registered.ConfigureTracing(options => options.Enabled = false);
        registered.AddDevices(devices => devices
            .AddClient("Chargers", new FakeDeviceServer().Transport, resolveAddress: ProtoDeviceAddress.Template("memory://cp-004"))
                .AddDevice<FakeCharger>());
        await using var registeredHost = registered.Build();
        await registeredHost.StartAsync();

        Assert.Multiple(() =>
        {
            Assert.That(attribute.Kind, Is.EqualTo(ProtoCapabilityKinds.Device));
            Assert.That(attribute.CapabilityName, Is.EqualTo(nameof(FakeCharger)));
            Assert.That(missing, Does.Contain(nameof(FakeCharger)));
            Assert.That(attribute.GetSkipReason(registeredHost), Is.Null);
        });

        await host.StopAsync();
        await registeredHost.StopAsync();
    }

    [Test]
    public async Task Devices_WithoutAClient_ShouldExplain()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddDevices();
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("no clients", "00001", TestMethods.Placeholder);

        var exception = Assert.Throws<InvalidOperationException>(() => Proto.Context.Devices());

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.That(exception!.Message, Does.Contain("No device client is registered"));
    }

    private static string TemporaryTracePath()
        => Path.Combine(Path.GetTempPath(), $"prototest-devices-{Guid.NewGuid():N}.prototrace");

    private sealed class FakeCharger : ProtoDevice
    {
        public ValueTask<string> BootAsync() => ExchangeAsync("BOOT", "BOOT_ACK", TimeSpan.FromSeconds(5));

        public ValueTask<string> AwaitAsync(string expected, TimeSpan timeout) => ExpectTextAsync(expected, timeout);

        public ValueTask SendTextAsync(string text) => SendAsync(DeviceFrame.Text(text));

        public ValueTask CloseAsync() => DisconnectAsync();

        private async ValueTask<string> ExchangeAsync(string send, string expect, TimeSpan timeout)
        {
            await SendAsync(DeviceFrame.Text(send));
            return await ExpectTextAsync(expect, timeout);
        }

        private async ValueTask<string> ExpectTextAsync(string expected, TimeSpan timeout)
        {
            var frame = await ExpectAsync(
                $"server sends {expected}",
                candidate => candidate.TryGetText(out var text) && text == expected,
                timeout);
            return frame.AsText();
        }
    }

    private sealed class FakeProtocol : IProtoDeviceProtocol
    {
        public string Name => "Fake protocol";

        public IReadOnlyList<DeviceProtocolEntry> Entries { get; } =
        [
            new("BOOT_ACK"),
            new("METER_ACK"),
            new("PLUG_ACK")
        ];

        public string? Classify(DeviceFrame frame) => frame.TryGetText(out var text) ? text : null;
    }

    private sealed class FakeDeviceServer
    {
        private readonly List<string> _sent = [];

        public FakeProtocol Protocol { get; } = new();

        public bool Responds { get; init; } = true;

        public InMemoryTransport Transport { get; }

        public IReadOnlyList<string> Sent
        {
            get
            {
                lock (_sent)
                {
                    return [.. _sent];
                }
            }
        }

        public FakeDeviceServer()
        {
            Transport = new InMemoryTransport(Connect);
        }

        private InMemoryConnection Connect(DeviceEndpoint endpoint)
            => new(Protocol, Responds, frame =>
            {
                lock (_sent)
                {
                    _sent.Add(frame);
                }
            });
    }

    private sealed class InMemoryTransport(Func<DeviceEndpoint, InMemoryConnection> connect) : IProtoDeviceTransport
    {
        public string Name => "InMemory";

        public DeviceEndpoint? LastEndpoint { get; private set; }

        public ValueTask<IProtoDeviceConnection> ConnectAsync(DeviceEndpoint endpoint, CancellationToken cancellationToken = default)
        {
            LastEndpoint = endpoint;
            return ValueTask.FromResult<IProtoDeviceConnection>(connect(endpoint));
        }
    }

    private sealed class InMemoryConnection(
        IProtoDeviceProtocol protocol,
        bool responds,
        Action<string> onSent) : IProtoDeviceConnection
    {
        private readonly Channel<DeviceFrame> _incoming = Channel.CreateUnbounded<DeviceFrame>();

        public string? RemoteAddress => "memory://";

        public ValueTask SendAsync(DeviceFrame frame, CancellationToken cancellationToken = default)
        {
            var text = frame.TryGetText(out var value) ? value : frame.ToString();
            onSent(text);
            if (responds && protocol.Classify(frame) is { } kind)
            {
                _incoming.Writer.TryWrite(DeviceFrame.Text($"{kind}_ACK"));
            }

            return ValueTask.CompletedTask;
        }

        public async ValueTask<DeviceFrame?> ReceiveAsync(CancellationToken cancellationToken = default)
            => await _incoming.Reader.ReadAsync(cancellationToken);

        public ValueTask DisposeAsync()
        {
            _incoming.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}
