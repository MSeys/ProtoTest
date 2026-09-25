namespace ProtoTest.Devices.WebSocket.Tests;

using System.Net;
using System.Net.WebSockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ProtoTest.Core;

[TestFixture]
public sealed class WebSocketDeviceTests
{
    [Test]
    public async Task WebSocketClient_ShouldExchangeTextFramesWithATypedDevice()
    {
        await using var server = await EchoServer.StartAsync(RespondAsync);
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddDevices(devices => devices
            .AddWebSocketClient("Chargers", address: server.Address, path: "/ws/{deviceId}")
                .AddDevice<EchoDevice>());
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("websocket", "00001", TestMethods.Placeholder);

        var device = Proto.Context.Devices("Chargers").For<EchoDevice>("CP-001");
        var ack = await device.BootAsync();

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        var entity = host.Trace.Snapshot().Tests.Single().Entities!
            .Single(candidate => candidate.Kind == ProtoTraceEntityKinds.Device);
        Assert.Multiple(() =>
        {
            Assert.That(ack, Is.EqualTo("BOOT_ACK"));
            Assert.That(entity.State["device.client"], Is.EqualTo("Chargers"));
            Assert.That(entity.State["device.transport"], Is.EqualTo("WebSocket"));
            Assert.That(entity.State["device.address"], Is.EqualTo($"{server.Address}/ws/CP-001"));
            Assert.That(
                entity.State["device.connected"],
                Is.EqualTo("false"),
                "the release path disconnects the device and finalises its state");
        });
    }

    [Test]
    public async Task WebSocketClient_ShouldRoundTripBinaryFrames()
    {
        await using var server = await EchoServer.StartAsync(RespondAsync);
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddDevices(devices => devices
            .AddWebSocketClient("Chargers", address: server.Address, path: "/ws/{deviceId}")
                .AddDevice<EchoDevice>());
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("binary", "00001", TestMethods.Placeholder);

        var device = Proto.Context.Devices("Chargers").For<EchoDevice>("CP-002");
        var payload = new byte[] { 0x01, 0x02, 0xFE, 0xFF };
        var echoed = await device.EchoBinaryAsync(payload);

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.That(echoed, Is.EqualTo(payload));
    }

    [Test]
    public async Task WebSocketClient_WhenConnectFails_ShouldNameTheAddress()
    {
        var port = TestNetworking.FreePort();
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddDevices(devices => devices
            .AddWebSocketClient("Chargers", address: $"ws://127.0.0.1:{port}/ws")
                .AddDevice<EchoDevice>());
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("connect failure", "00001", TestMethods.Placeholder);

        var device = Proto.Context.Devices("Chargers").For<EchoDevice>("CP-003");
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await device.BootAsync());

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain($"127.0.0.1:{port}"));
            Assert.That(exception.Message, Does.Contain("could not connect"));
        });
    }

    [Test]
    public async Task WebSocketClient_WhenTheDeviceCloses_ShouldReportIt()
    {
        await using var server = await EchoServer.StartAsync(async socket =>
        {
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, statusDescription: null, CancellationToken.None);
        });
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddDevices(devices => devices
            .AddWebSocketClient("Chargers", address: server.Address, path: "/ws/{deviceId}")
                .AddDevice<EchoDevice>());
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("closed by device", "00001", TestMethods.Placeholder);

        var device = Proto.Context.Devices("Chargers").For<EchoDevice>("CP-004");
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await device.BootAsync());

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        await host.StopAsync();

        Assert.That(exception!.Message, Does.Contain("closed the connection"));
    }

    private static async Task RespondAsync(WebSocket socket)
    {
        var buffer = new byte[4096];
        while (socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, statusDescription: null, CancellationToken.None);
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

    private sealed class EchoServer : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private EchoServer(WebApplication app, string address)
        {
            _app = app;
            Address = address;
        }

        public string Address { get; }

        public static async Task<EchoServer> StartAsync(Func<WebSocket, Task> handler)
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
                    await handler(socket);
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
            return new EchoServer(app, $"ws://127.0.0.1:{new Uri(address).Port}");
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
