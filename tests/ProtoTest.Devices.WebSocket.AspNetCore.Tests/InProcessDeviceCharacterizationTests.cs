namespace ProtoTest.Devices.WebSocket.AspNetCore.Tests;

using ProtoTest.AspNetCore;
using ProtoTest.Core;
using ProtoTest.Devices;
using SampleApi = ProtoTest.AspNetCore.SampleApi;

/// <summary>
/// Pins the in-process WebSocket device transport before the audit fixes it (audit Stage A0):
/// registrations collapse to one non-generic marker, <c>CanConnect</c> ignores the endpoint, and the
/// configured transport options are registered but never consumed or validated.
/// </summary>
[TestFixture]
[Category("Characterization")]
public sealed class InProcessDeviceCharacterizationTests
{
    [Test]
    public async Task TwoInProcessRegistrations_ShouldKeepOnlyTheFirstTransport()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder
            .AddInProcessWebSocketDevices<SampleApi.Program>("Api")
            .AddInProcessWebSocketDevices<SampleApi.Program>("Second")
            // Only the second registration's application is hosted: a transport for "Second" would
            // connect here, while the surviving first transport looks up "Api" and finds nothing.
            .AddApplication("Second", app => app.AddAspNetCoreServer<SampleApi.Program>());
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("two in-process transports", "00001", TestMethods.Placeholder);

        var transports = Proto.Context.Service<IEnumerable<IProtoDeviceTransport>>().ToArray();
        var inProcess = transports.OfType<IProtoInProcessDeviceTransport>().ToArray();
        var reachingSecond = inProcess[0].CanConnect(
            Proto.Context,
            new DeviceEndpoint("CP-001", "/ws/CP-001"));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            // Pins current behavior; audit DEV-1 flips this once the marker is keyed by
            // (TProgram, application) and the transport is matched to the client's application.
            Assert.That(
                transports,
                Has.Length.EqualTo(1),
                "the second in-process transport is silently dropped by one non-generic marker");
            Assert.That(inProcess, Has.Length.EqualTo(1));
            Assert.That(
                reachingSecond,
                Is.False,
                "the surviving transport carries the first application name ('Api'), so it cannot reach 'Second', which is the only application hosted");
        });
    }

    [Test]
    public async Task CanConnect_ForAnUnrelatedEndpoint_ShouldReturnTrue()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder
            .AddInProcessWebSocketDevices<SampleApi.Program>("Api")
            .AddApplication("Api", app => app.AddAspNetCoreServer<SampleApi.Program>());
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("endpoint-blind transport", "00001", TestMethods.Placeholder);

        var transport = Proto.Context.Service<IEnumerable<IProtoDeviceTransport>>()
            .OfType<InProcessWebSocketDeviceTransport<SampleApi.Program>>()
            .Single();
        var reachable = transport.CanConnect(
            Proto.Context,
            new DeviceEndpoint("CP-001", "http://unrelated.example.test/definitely/not/a/route"));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        // Pins current behavior; audit DEV-1 flips this once CanConnect consults the endpoint or the
        // client's application instead of only this transport's own application.
        Assert.That(
            reachable,
            Is.True,
            "CanConnect only checks that the transport's application is hosted; the endpoint is not consulted");
    }

    [Test]
    public async Task InProcessOptions_ShouldNotBeValidatedOrConsumed()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder
            .AddInProcessWebSocketDevices<SampleApi.Program>(
                "Api",
                options =>
                {
                    // WebSocketDeviceOptions.Validate rejects both values.
                    options.ConnectTimeout = TimeSpan.Zero;
                    options.ReceiveBufferBytes = 16;
                })
            .AddApplication("Api", app => app
                .AddAspNetCoreServer<SampleApi.Program>()
                .AddDevices(devices => devices
                    // Registered by transport name on purpose: AddWebSocketClient would add the socket
                    // transport, whose construction resolves the options and validates them before the
                    // in-process path could be chosen.
                    .AddClient(
                        "Chargers",
                        InProcessWebSocketDeviceTransport<SampleApi.Program>.TransportName,
                        path: "/ws/{deviceId}")
                        .AddDevice<EchoDevice>()));
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("dead in-process options", "00001", TestMethods.Placeholder);

        var device = Proto.Context.Devices("Chargers").For<EchoDevice>("CP-001");
        var ack = await device.BootAsync();

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        // Pins current behavior; audit DEV-2 flips this once the in-process transport consumes the
        // registered options and validation runs (this configuration would then fail).
        Assert.That(ack, Is.EqualTo("BOOT_ACK"));
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
    }
}
