namespace ProtoTest.Devices.WebSocket.AspNetCore.Tests;

using ProtoTest.AspNetCore;
using ProtoTest.Core;
using ProtoTest.Devices;
using ProtoTest.Devices.WebSocket;
using SampleApi = ProtoTest.AspNetCore.SampleApi;

/// <summary>
/// Pins the in-process WebSocket device transport after the audit fixes it (audit DEV-1/DEV-2): the
/// registration is keyed by (program, application) so a second application keeps its transport,
/// <c>CanConnect</c> answers for the client's application instead of ignoring it, and the registered
/// <see cref="WebSocketDeviceOptions"/> are resolved and validated instead of being dead.
/// </summary>
[TestFixture]
[Category("Characterization")]
public sealed class InProcessDeviceCharacterizationTests
{
    [Test]
    public async Task TwoInProcessRegistrations_ShouldRegisterOneTransportPerApplication()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder
            .AddInProcessWebSocketDevices<SampleApi.Program>("Api")
            .AddInProcessWebSocketDevices<SampleApi.Program>("Second")
            // Only the second registration's application is hosted: a transport for "Second" would
            // connect here, while the transport for "Api" has no server to reach.
            .AddApplication("Second", app => app.AddAspNetCoreServer<SampleApi.Program>());
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("two in-process transports", "00001", TestMethods.Placeholder);

        var transports = Proto.Context.Service<IEnumerable<IProtoDeviceTransport>>().ToArray();
        var inProcess = transports.OfType<IProtoInProcessDeviceTransport>().ToArray();
        var secondReachable = inProcess.Any(transport => transport.CanConnect(Proto.Context, "Second"));
        var apiReachable = inProcess.Any(transport => transport.CanConnect(Proto.Context, "Api"));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(
                inProcess,
                Has.Length.EqualTo(2),
                "the registration is keyed by (program, application), so the second application is not dropped");
            Assert.That(secondReachable, Is.True, "the hosted application has a transport");
            Assert.That(apiReachable, Is.False, "application 'Api' is not hosted, so no transport claims it");
        });
    }

    [Test]
    public async Task CanConnect_ForAnotherApplication_ShouldReturnFalse()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder
            .AddInProcessWebSocketDevices<SampleApi.Program>("Api")
            .AddApplication("Api", app => app.AddAspNetCoreServer<SampleApi.Program>());
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("identity-checked transport", "00001", TestMethods.Placeholder);

        var transport = Proto.Context.Service<IEnumerable<IProtoDeviceTransport>>()
            .OfType<InProcessWebSocketDeviceTransport<SampleApi.Program>>()
            .Single();
        var ownApplication = transport.CanConnect(Proto.Context, "Api");
        var otherApplication = transport.CanConnect(Proto.Context, "Second");

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(ownApplication, Is.True, "the transport serves the application it was registered for");
            Assert.That(
                otherApplication,
                Is.False,
                "a client for another application is never routed through this application's TestServer, even at the same path");
        });
    }

    [Test]
    public async Task InProcessOptions_WhenConfiguredInvalidly_ShouldFailValidation()
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
                    // transport too, and this pin is about the in-process transport's own options.
                    .AddClient(
                        "Chargers",
                        InProcessWebSocketDeviceTransport<SampleApi.Program>.TransportName,
                        path: "/ws/{deviceId}")
                        .AddDevice<EchoDevice>()));
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("validated in-process options", "00001", TestMethods.Placeholder);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => Proto.Context.Devices("Chargers").For<EchoDevice>("CP-001"));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(exception!.ParamName, Is.EqualTo(nameof(WebSocketDeviceOptions.ConnectTimeout)));
            Assert.That(exception.Message, Does.Contain("must be positive"));
        });
    }

    [Test]
    public async Task Connect_FromAFlowWithoutAmbientContext_ShouldReachTheRegisteredApplication()
    {
        // Audit 5 A5-63 (D-11): routing was decided from the session's context; the connect must use
        // that context instead of re-reading the ambient host state of whatever flow runs it.
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder
            .AddInProcessWebSocketDevices<SampleApi.Program>("Api")
            .AddApplication("Api", app => app
                .AddAspNetCoreServer<SampleApi.Program>()
                .AddDevices(devices => devices
                    .AddClient(
                        "Chargers",
                        InProcessWebSocketDeviceTransport<SampleApi.Program>.TransportName,
                        path: "/ws/{deviceId}")
                        .AddDevice<EchoDevice>()));
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("in-process device off-flow", "00001", TestMethods.Placeholder);

        var device = Proto.Context.Devices("Chargers").For<EchoDevice>("CP-001");
        string ack;
        // The task starts with no captured flow, so the ambient Proto.Context is absent; the session's
        // own context must open the connection (before the fix this threw "outside one"). The
        // suppression is undone on this same thread because the block never awaits.
        using (ExecutionContext.SuppressFlow())
        {
            ack = Task.Run(() => device.BootAsync().AsTask()).GetAwaiter().GetResult();
        }

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.That(ack, Is.EqualTo("BOOT_ACK"));
    }

    [Test]
    public async Task InProcessTransport_ShouldUseTheRegisteredOptionsInstance()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder
            .AddInProcessWebSocketDevices<SampleApi.Program>(
                "Api",
                options =>
                {
                    options.ConnectTimeout = TimeSpan.FromSeconds(30);
                    options.ReceiveBufferBytes = 4096;
                })
            .AddApplication("Api", app => app
                .AddAspNetCoreServer<SampleApi.Program>()
                .AddDevices(devices => devices
                    .AddClient(
                        "Chargers",
                        InProcessWebSocketDeviceTransport<SampleApi.Program>.TransportName,
                        path: "/ws/{deviceId}")
                        .AddDevice<EchoDevice>()));
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("live in-process options", "00001", TestMethods.Placeholder);

        var registered = Proto.Context.Service<WebSocketDeviceOptions>();
        var transport = Proto.Context.Service<IEnumerable<IProtoDeviceTransport>>()
            .OfType<InProcessWebSocketDeviceTransport<SampleApi.Program>>()
            .Single();

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(transport.Options, Is.SameAs(registered), "the transport connects and reads with the registered options");
            Assert.That(registered.ConnectTimeout, Is.EqualTo(TimeSpan.FromSeconds(30)));
            Assert.That(registered.ReceiveBufferBytes, Is.EqualTo(4096));
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
    }
}
