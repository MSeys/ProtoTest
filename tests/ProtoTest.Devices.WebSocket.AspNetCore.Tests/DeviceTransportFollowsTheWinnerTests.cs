namespace ProtoTest.Devices.WebSocket.AspNetCore.Tests;

using ProtoTest.AspNetCore;
using ProtoTest.Core;
using ProtoTest.Devices;
using ProtoTest.Devices.WebSocket;
using SampleApi = ProtoTest.AspNetCore.SampleApi;

/// <summary>
/// The in-process device transport follows the application's provider chain: it serves a TestServer
/// winner, and a loopback winner drops its capability and routes the same client over the socket at
/// the published address.
/// </summary>
[TestFixture]
public sealed class DeviceTransportFollowsTheWinnerTests
{
    [Test]
    public async Task InProcessTransport_WhenTheChainServesTheApplicationInProcess_ShouldServeThroughTheTestServer()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddInProcessWebSocketDevices<SampleApi.Program>("Api");
        builder.AddApplication("Api", app => app
            .UseConfigured()
            .UseInProcess<SampleApi.Program>()
            .AddDevices(devices => devices
                .AddWebSocketClient("Chargers", path: "/ws/{deviceId}")
                    .AddDevice<EchoDevice>()));
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("in-process winner", "00001", TestMethods.Placeholder);

        var ack = await Proto.Context.Devices("Chargers").For<EchoDevice>("CP-001").BootAsync();
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
            Assert.That(
                host.HasCapability(ProtoCapabilityKinds.Device, InProcessWebSocketDeviceTransport<SampleApi.Program>.TransportName),
                Is.True);
        });
    }

    [Test]
    public async Task InProcessTransport_WhenTheLoopbackProviderWins_ShouldDropTheCapabilityAndUseTheSocket()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddInProcessWebSocketDevices<SampleApi.Program>("Api");
        builder.AddApplication("Api", app => app
            .UseConfigured()
            .UseLoopback(SampleApi.Program.CreateApp)
            .AddDevices(devices => devices
                .AddWebSocketClient("Chargers", path: "/ws/{deviceId}")
                    .AddDevice<EchoDevice>()));
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("loopback winner", "00001", TestMethods.Placeholder);

        var ack = await Proto.Context.Devices("Chargers").For<EchoDevice>("CP-002").BootAsync();
        var address = ProtoApplication.BaseUrl(Proto.Context, "Api");
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        var entries = host.Trace.Snapshot().Entries!;
        var resolved = entries.Single(entry => entry.Kind == ProtoTargetTrace.Resolved);
        var skipped = entries.Single(entry => entry.Kind == "capability.skipped");
        var entity = host.Trace.Snapshot().Tests.Single().Entities!
            .Single(candidate => candidate.Kind == ProtoTraceEntityKinds.Device);
        Assert.Multiple(() =>
        {
            Assert.That(ack, Is.EqualTo("BOOT_ACK"), "the socket at the loopback address serves the device");
            Assert.That(address, Does.StartWith("http://127.0.0.1:"));
            Assert.That(entity.State["device.transport"], Is.EqualTo("WebSocket"));
            Assert.That(entity.State["device.address"], Does.StartWith("ws://127.0.0.1:").And.EndWith("/ws/CP-002"));
            Assert.That(resolved.Attributes["environment.provider"], Is.EqualTo("loopback"));
            Assert.That(
                host.HasCapability(ProtoCapabilityKinds.Device, InProcessWebSocketDeviceTransport<SampleApi.Program>.TransportName),
                Is.False,
                "a loopback application is not served by the in-process server");
            Assert.That(
                skipped.Attributes["capability.reason"],
                Does.Contain("does not run it in-process"),
                "the skip names the winning provider instead of the addressed key");
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
