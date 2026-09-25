namespace ProtoTest.Devices.WebSocket.AspNetCore.Tests;

using ProtoTest.AspNetCore;
using ProtoTest.Core;
using ProtoTest.Devices;
using SampleApi = ProtoTest.AspNetCore.SampleApi;
using SecondApi = ProtoTest.AspNetCore.SecondTestApi;

/// <summary>
/// Pins multi-application device routing (audit DEV-1): two applications host the same path, and each
/// device client reaches the application it was registered under instead of the first transport that
/// happens to be registered.
/// </summary>
[TestFixture]
public sealed class MultiApplicationDeviceRoutingTests
{
    [Test]
    public async Task TwoApplications_WithTheSamePath_ShouldEachServeTheirOwnDeviceClient()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder
            .AddInProcessWebSocketDevices<SampleApi.Program>("Api")
            .AddInProcessWebSocketDevices<SecondApi.Program>("Second")
            .AddApplication("Api", app => app
                .AddAspNetCoreServer<SampleApi.Program>()
                .AddDevices(devices => devices
                    .AddWebSocketClient("ApiChargers", path: "/ws/{deviceId}")
                        .AddDevice<EchoDevice>()))
            .AddApplication("Second", app => app
                .AddAspNetCoreServer<SecondApi.Program>()
                .AddDevices(devices => devices
                    .AddWebSocketClient("SecondChargers", path: "/ws/{deviceId}")
                        .AddDevice<EchoDevice>()));
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("two applications, same path", "00001", TestMethods.Placeholder);

        var apiDevice = Proto.Context.Devices("ApiChargers").For<EchoDevice>("CP-001");
        var secondDevice = Proto.Context.Devices("SecondChargers").For<EchoDevice>("CP-001");
        var apiAck = await apiDevice.BootAsync();
        var secondAck = await secondDevice.BootAsync();

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(apiAck, Is.EqualTo("BOOT_ACK"), "the client under 'Api' reached the first application");
            Assert.That(
                secondAck,
                Is.EqualTo($"{SecondApi.Program.EchoPrefix}BOOT_ACK"),
                "the client under 'Second' reached the second application, not the first one at the same path");
        });
    }

    private sealed class EchoDevice : ProtoDevice
    {
        public async ValueTask<string> BootAsync()
        {
            await SendTextAsync("BOOT");
            var frame = await ExpectAsync(
                "server acknowledges boot",
                candidate => candidate.TryGetText(out var text) && text.EndsWith("BOOT_ACK", StringComparison.Ordinal),
                TimeSpan.FromSeconds(5));
            return frame.AsText();
        }
    }
}
