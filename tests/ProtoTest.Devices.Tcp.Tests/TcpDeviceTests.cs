namespace ProtoTest.Devices.Tcp.Tests;

using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Configuration;
using ProtoTest.Core;
using ProtoTest.Devices.Exceptions;
using ProtoTest.TestSupport;

[TestFixture]
public sealed class TcpDeviceTests
{
    [Test]
    public async Task TcpClient_ShouldExchangeLinesWithAPeerAndTraceEachFrame()
    {
        using var trace = new TemporaryTrace("tcp-client");
        await using var peer = LinePeer.Start(line => line == "BOOT M-1" ? "BOOT_ACK" : null);
        await using var host = await StartHostAsync(
            devices => devices
                .AddTcpClient("Meters", DeviceFramers.Lines("\r\n"), address: peer.Address)
                .AddDevice<Meter>(),
            trace);
        await host.StartTestAsync("tcp client", "00001", TestMethods.Placeholder);

        var meter = Proto.Context.Devices("Meters").For<Meter>("M-1");
        var ack = await meter.BootAsync();

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
        var test = host.Trace.Snapshot().Tests.Single();
        var entity = test.Entities!.Single(candidate => candidate.Kind == ProtoTraceEntityKinds.Device);
        Assert.Multiple(() =>
        {
            Assert.That(ack, Is.EqualTo("BOOT_ACK"));
            Assert.That(peer.Received, Is.EqualTo(new[] { "BOOT M-1" }), "the line went out with its CRLF");
            Assert.That(entity.State["device.transport"], Is.EqualTo("TCP"));
            Assert.That(entity.State["device.address"], Is.EqualTo(peer.Address));
            Assert.That(test.Entries.Select(entry => entry.Kind), Does.Contain("device.send").And.Contain("device.command"));
        });
    }

    [Test]
    public async Task TcpClient_ShouldUseTheClientsFramer()
    {
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        var address = $"tcp://127.0.0.1:{((IPEndPoint)server.LocalEndpoint).Port}";
        var answered = Task.Run(async () =>
        {
            using var socket = await server.AcceptTcpClientAsync();
            var stream = socket.GetStream();
            var header = new byte[2];
            await stream.ReadExactlyAsync(header);
            var payload = new byte[header[0] << 8 | header[1]];
            await stream.ReadExactlyAsync(payload);
            await stream.WriteAsync(new byte[] { 0, 2, (byte)'O', (byte)'K' });
            return Encoding.ASCII.GetString(payload);
        });
        await using var host = await StartHostAsync(devices => devices
            .AddTcpClient("Meters", DeviceFramers.LengthPrefixed(headerBytes: 2), address: address)
            .AddDevice<Meter>());
        await host.StartTestAsync("tcp framer", "00001", TestMethods.Placeholder);

        var reply = await Proto.Context.Devices("Meters").For<Meter>("M-1").AskBinaryAsync("PING");

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.Multiple(async () =>
        {
            Assert.That(await answered, Is.EqualTo("PING"));
            Assert.That(reply, Is.EqualTo("OK"));
        });
    }

    [Test]
    public async Task TcpClient_WhenNothingListens_ShouldNameTheDeviceAndAddress()
    {
        var closedPort = TestNetworking.FreePort();
        await using var host = await StartHostAsync(devices => devices
            .AddTcpClient("Meters", address: $"tcp://127.0.0.1:{closedPort}")
            .AddDevice<Meter>());
        await host.StartTestAsync("tcp refused", "00001", TestMethods.Placeholder);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Proto.Context.Devices("Meters").For<Meter>("M-1").BootAsync());

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        Assert.That(exception!.Message, Does.Contain("'M-1' could not connect to 'tcp://127.0.0.1:").And.Contain("TCP"));
    }

    [Test]
    public void TcpClient_WithAnAddressThatIsNotTcp_ShouldFailAtRegistration()
        => Assert.That(
            () => new ProtoHostBuilder().AddDevices(devices => devices.AddTcpClient("Meters", address: "http://127.0.0.1:7000")),
            Throws.InvalidOperationException.With.Message.Contains("use tcp://host:port"));

    [Test]
    public async Task TcpListener_ShouldWaitForTheSystemUnderTestToConnect()
    {
        using var trace = new TemporaryTrace("tcp-listener");
        await using var host = await StartHostAsync(
            devices => devices.AddTcpListener("Meters", DeviceFramers.Lines()).AddDevice<Meter>(),
            trace);
        await host.StartTestAsync("tcp listener", "00001", TestMethods.Placeholder);
        var meter = Proto.Context.Devices("Meters").For<Meter>("M-1");

        var address = await meter.OpenPortAsync();
        var (hostName, port) = TcpDeviceAddress.Parse(address);
        using var application = new TcpClient();
        await application.ConnectAsync(hostName, port);
        var stream = application.GetStream();
        await stream.WriteAsync("READ\n"u8.ToArray());
        var request = await meter.ReceiveLineAsync();
        await meter.SendLineAsync("$MTR,M-1,12.50");
        var reply = await new StreamReader(stream).ReadLineAsync();

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
        var test = host.Trace.Snapshot().Tests.Single();
        Assert.Multiple(() =>
        {
            Assert.That(address, Does.StartWith("tcp://127.0.0.1:"));
            Assert.That(port, Is.GreaterThan(0), "the operating system chose the port");
            Assert.That(request, Is.EqualTo("READ"));
            Assert.That(reply, Is.EqualTo("$MTR,M-1,12.50"));
            Assert.That(test.Entries.Select(entry => entry.Kind), Does.Contain("device.listen"));
        });
    }

    [Test]
    public async Task TcpListener_ShouldGiveEachDeviceItsOwnPort()
    {
        await using var host = await StartHostAsync(devices => devices.AddTcpListener("Meters").AddDevice<Meter>());
        await host.StartTestAsync("tcp ports", "00001", TestMethods.Placeholder);

        var first = await Proto.Context.Devices("Meters").For<Meter>("M-1").OpenPortAsync();
        var second = await Proto.Context.Devices("Meters").For<Meter>("M-2").OpenPortAsync();

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.That(first, Is.Not.EqualTo(second));
    }

    [Test]
    public async Task TcpListener_WhenNothingConnects_ShouldSayWhereToHandTheAddress()
    {
        await using var host = await StartHostAsync(
            devices => devices.AddTcpListener("Meters", configure: options => options.AcceptTimeout = TimeSpan.FromMilliseconds(200)).AddDevice<Meter>());
        await host.StartTestAsync("tcp accept timeout", "00001", TestMethods.Placeholder);
        var meter = Proto.Context.Devices("Meters").For<Meter>("M-1");
        var address = await meter.OpenPortAsync();

        var exception = Assert.ThrowsAsync<TimeoutException>(async () => await meter.ReceiveLineAsync());

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        Assert.That(exception!.Message, Does.Contain($"Nothing connected to '{address}'").And.Contain("ListenAsync"));
    }

    [Test]
    public async Task TcpClient_WhenThePeerClosesMidLine_ShouldReportAFramingError()
    {
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        var address = $"tcp://127.0.0.1:{((IPEndPoint)server.LocalEndpoint).Port}";
        var closing = Task.Run(async () =>
        {
            using var socket = await server.AcceptTcpClientAsync();
            await socket.GetStream().WriteAsync("$MTR,M-1"u8.ToArray());
        });
        await using var host = await StartHostAsync(devices => devices.AddTcpClient("Meters", address: address).AddDevice<Meter>());
        await host.StartTestAsync("tcp mid-frame", "00001", TestMethods.Placeholder);
        var meter = Proto.Context.Devices("Meters").For<Meter>("M-1");
        await meter.ConnectNowAsync();
        await closing;

        var exception = Assert.ThrowsAsync<DeviceFramingException>(async () => await meter.ReceiveLineAsync());

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        Assert.That(exception!.Message, Does.Contain("in the middle of a frame").And.Contain("8 bytes"));
    }

    [Test]
    public async Task TcpOptions_ShouldBindFromConfiguration()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ProtoTest:Devices:Tcp:AcceptTimeout"] = "00:00:00.150"
        }));
        builder.AddDevices(devices => devices.AddTcpListener("Meters").AddDevice<Meter>());
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("tcp options", "00001", TestMethods.Placeholder);
        var meter = Proto.Context.Devices("Meters").For<Meter>("M-1");
        await meter.OpenPortAsync();

        var exception = Assert.ThrowsAsync<TimeoutException>(async () => await meter.ReceiveLineAsync());

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        Assert.That(exception!.Message, Does.Contain("within 0.1s").Or.Contain("within 0.2s"));
    }

    private static async Task<ProtoHost> StartHostAsync(Action<ProtoDeviceBuilder> configure, TemporaryTrace? trace = null)
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options =>
        {
            options.Enabled = trace is not null;
            if (trace is not null)
            {
                options.OutputPath = trace.Path;
            }
        });
        builder.AddDevices(configure);
        var host = builder.Build();
        await host.StartAsync();
        return host;
    }

    private sealed class Meter : ProtoDevice
    {
        public async Task<string> BootAsync()
        {
            await SendTextAsync($"BOOT {Id}");
            var ack = await ExpectAsync("Boot acknowledged", frame => frame.TryGetText(out var text) && text == "BOOT_ACK");
            return ack.AsText();
        }

        public async Task<string> AskBinaryAsync(string question)
        {
            await SendAsync(DeviceFrame.Binary(Encoding.ASCII.GetBytes(question)));
            var reply = await ReceiveAsync();
            return Encoding.ASCII.GetString(reply.Payload.Span);
        }

        public ValueTask<string> OpenPortAsync() => ListenAsync();

        public ValueTask ConnectNowAsync() => ConnectAsync();

        public ValueTask SendLineAsync(string line) => SendTextAsync(line);

        public async Task<string> ReceiveLineAsync() => (await ReceiveAsync()).AsText();
    }

    /// <summary>A loopback TCP peer that reads CRLF lines and answers the ones its responder knows.</summary>
    private sealed class LinePeer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly Task _serve;
        private readonly List<string> _received = [];

        private LinePeer(Func<string, string?> respond)
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Address = $"tcp://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
            _serve = ServeAsync(respond);
        }

        public string Address { get; }

        public IReadOnlyList<string> Received
        {
            get
            {
                lock (_received)
                {
                    return [.. _received];
                }
            }
        }

        public static LinePeer Start(Func<string, string?> respond) => new(respond);

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            try
            {
                await _serve;
            }
            catch (Exception exception) when (exception is SocketException or ObjectDisposedException or IOException)
            {
                // The test ended before or while the device was connected.
            }
        }

        private async Task ServeAsync(Func<string, string?> respond)
        {
            using var client = await _listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            while (await reader.ReadLineAsync() is { } line)
            {
                lock (_received)
                {
                    _received.Add(line);
                }

                if (respond(line) is { } answer)
                {
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(answer + "\r\n"));
                }
            }
        }
    }
}
