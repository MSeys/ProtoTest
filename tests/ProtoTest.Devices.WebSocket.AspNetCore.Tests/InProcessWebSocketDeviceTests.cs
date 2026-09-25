namespace ProtoTest.Devices.WebSocket.AspNetCore.Tests;

using System.Net;
using System.Net.WebSockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ProtoTest.AspNetCore;
using ProtoTest.Core;
using SampleApi = ProtoTest.AspNetCore.SampleApi;

[TestFixture]
public sealed class InProcessWebSocketDeviceTests
{
    [Test]
    public async Task OneClientRegistration_ShouldUseTheInProcessEndpointWhenTheApplicationIsHosted()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder
            .AddInProcessWebSocketDevices<SampleApi.Program>("Api")
            .AddApplication("Api", app => app
                .AddAspNetCoreServer<SampleApi.Program>()
                .AddDevices(devices => devices
                    .AddWebSocketClient("Chargers", path: "/ws/{deviceId}")
                        .AddDevice<EchoDevice>()));
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("in-process device", "00001", TestMethods.Placeholder);

        var device = Proto.Context.Devices("Chargers").For<EchoDevice>("CP-001");
        var ack = await device.BootAsync();

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        var entity = host.Trace.Snapshot().Tests.Single().Entities!
            .Single(candidate => candidate.Kind == ProtoTraceEntityKinds.Device);
        Assert.Multiple(() =>
        {
            Assert.That(ack, Is.EqualTo("BOOT_ACK"));
            Assert.That(
                entity.State["device.transport"],
                Is.EqualTo(InProcessWebSocketDeviceTransport<SampleApi.Program>.TransportName));
            Assert.That(entity.State["device.address"], Is.EqualTo("/ws/CP-001"));
            Assert.That(entity.State["device.connected"], Is.EqualTo("true"));
        });
    }

    [Test]
    public async Task TheSameClientRegistration_ShouldUseTheSocketWhenTheApplicationIsPublished()
    {
        await using var server = await KestrelEchoServer.StartAsync();
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:Api:BaseUrl"] = server.Address
            }));
        builder
            .AddInProcessWebSocketDevices<SampleApi.Program>("Api")
            .AddApplication("Api", app => app
                // The same registration in every mode: with an address configured the server steps
                // aside, and the device client falls back to the socket.
                .AddAspNetCoreServer<SampleApi.Program>()
                .AddDevices(devices => devices
                    .AddWebSocketClient("Chargers", path: "/ws/{deviceId}")
                        .AddDevice<EchoDevice>()));
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("published device", "00001", TestMethods.Placeholder);

        var device = Proto.Context.Devices("Chargers").For<EchoDevice>("CP-002");
        var ack = await device.BootAsync();

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        var entity = host.Trace.Snapshot().Tests.Single().Entities!
            .Single(candidate => candidate.Kind == ProtoTraceEntityKinds.Device);
        Assert.Multiple(() =>
        {
            Assert.That(ack, Is.EqualTo("BOOT_ACK"), "without an in-process server the address is used");
            Assert.That(entity.State["device.transport"], Is.EqualTo("WebSocket"));
            Assert.That(entity.State["device.address"], Is.EqualTo($"{server.DeviceAddress}/ws/CP-002"));
        });
    }

    [Test]
    public async Task OneClientRegistration_ShouldRoundTripBinaryFramesInProcess()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder
            .AddInProcessWebSocketDevices<SampleApi.Program>("Api")
            .AddApplication("Api", app => app
                .AddAspNetCoreServer<SampleApi.Program>()
                .AddDevices(devices => devices
                    .AddWebSocketClient("Chargers", path: "/ws/{deviceId}")
                        .AddDevice<EchoDevice>()));
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("in-process binary", "00001", TestMethods.Placeholder);

        var device = Proto.Context.Devices("Chargers").For<EchoDevice>("CP-003");
        var payload = new byte[] { 0x0A, 0x0B, 0xFD, 0xFE };
        var echoed = await device.EchoBinaryAsync(payload);

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.That(echoed, Is.EqualTo(payload));
    }

    [Test]
    public async Task InProcessTransportCapability_WhenTheApplicationIsHosted_ShouldBeDeclared()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder
            .AddInProcessWebSocketDevices<SampleApi.Program>("Api")
            .AddApplication("Api", app => app.AddAspNetCoreServer<SampleApi.Program>());
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StopAsync();

        Assert.That(
            host.HasCapability(
                ProtoCapabilityKinds.Device,
                InProcessWebSocketDeviceTransport<SampleApi.Program>.TransportName),
            Is.True,
            "the in-process transport serves the hosted application");
    }

    [Test]
    public async Task InProcessTransportCapability_WhenTheApplicationIsPublished_ShouldBeDropped()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:Api:BaseUrl"] = "https://published.example.test"
            }));
        builder
            .AddInProcessWebSocketDevices<SampleApi.Program>("Api")
            .AddApplication("Api", app => app.AddAspNetCoreServer<SampleApi.Program>());
        await using var host = builder.Build();
        await host.StartAsync();
        var skipped = host.Trace.Snapshot().Entries!
            .Single(entry => entry.Kind == "capability.skipped"
                && entry.Attributes["capability.name"] == InProcessWebSocketDeviceTransport<SampleApi.Program>.TransportName);
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(
                host.HasCapability(
                    ProtoCapabilityKinds.Device,
                    InProcessWebSocketDeviceTransport<SampleApi.Program>.TransportName),
                Is.False,
                "a published application is served over the socket, so the in-process capability is absent");
            Assert.That(skipped.Attributes["capability.keys"], Does.Contain("ProtoTest:Applications:Api:BaseUrl"));
        });
    }

    private sealed class EchoDevice : ProtoDevice
    {
        public async ValueTask<string> BootAsync()
        {
            await SendTextAsync("BOOT");
            var frame = await ExpectAsync(
                "server sends BOOT_ACK",
                candidate => candidate.TryGetText(out var text) && text == "BOOT_ACK",
                TimeSpan.FromSeconds(5));
            return frame.AsText();
        }

        public async ValueTask<byte[]> EchoBinaryAsync(byte[] payload)
        {
            await SendAsync(DeviceFrame.Binary(payload));
            var frame = await ExpectAsync(
                "server echoes the bytes",
                candidate => candidate.Payload.Span.SequenceEqual(payload),
                TimeSpan.FromSeconds(5));
            return frame.Payload.ToArray();
        }
    }

    private sealed class KestrelEchoServer : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private KestrelEchoServer(WebApplication app, string address, string deviceAddress)
        {
            _app = app;
            Address = address;
            DeviceAddress = deviceAddress;
        }

        /// <summary>The application address an HTTP consumer would use.</summary>
        public string Address { get; }

        /// <summary>The same address as the device client transforms it: http becomes ws.</summary>
        public string DeviceAddress { get; }

        public static async Task<KestrelEchoServer> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            var app = builder.Build();
            app.UseWebSockets();
            app.Map("/ws/{deviceId}", async context =>
            {
                var socket = await context.WebSockets.AcceptWebSocketAsync();
                try
                {
                    var buffer = new byte[4096];
                    while (socket.State == WebSocketState.Open)
                    {
                        var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            return;
                        }

                        var payload = buffer.AsSpan(0, result.Count).ToArray();
                        if (result.MessageType == WebSocketMessageType.Text)
                        {
                            var text = System.Text.Encoding.UTF8.GetString(payload);
                            var response = text == "BOOT" ? "BOOT_ACK" : $"{text}_ACK";
                            await socket.SendAsync(
                                System.Text.Encoding.UTF8.GetBytes(response),
                                WebSocketMessageType.Text,
                                endOfMessage: true,
                                CancellationToken.None);
                        }
                        else
                        {
                            await socket.SendAsync(payload, WebSocketMessageType.Binary, endOfMessage: true, CancellationToken.None);
                        }
                    }
                }
                finally
                {
                    socket.Dispose();
                }
            });
            await app.StartAsync();
            var address = app.Services
                .GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses.First();
            var host = $"127.0.0.1:{new Uri(address).Port}";
            return new KestrelEchoServer(app, $"http://{host}", $"ws://{host}");
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
