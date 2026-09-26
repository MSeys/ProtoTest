namespace ProtoTest.Devices.WebSocket.AspNetCore.Tests;

using System.Globalization;
using ProtoTest.AspNetCore;
using ProtoTest.Core;
using ProtoTest.Devices;
using SampleApi = ProtoTest.AspNetCore.SampleApi;

/// <summary>
/// Pins the clock an in-process application resolves while a device is connected: the handshake carries
/// the test id the in-process HTTP client carries, so the application serves the test's clock, and a
/// handshake with no test on its flow keeps the run-clock fallback.
/// </summary>
[TestFixture]
public sealed class InProcessWebSocketClockTests
{
    [Test]
    public async Task InProcessDevice_ShouldServeTheTestsClock_WhenTheTestClockAdvances()
    {
        var seed = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
        await using var host = ClockDeviceApplication(seed).Build();
        await host.StartAsync();
        await host.StartTestAsync("in-process device clock", "00001", TestMethods.Placeholder);

        var device = Proto.Context.Devices("Chargers").For<ClockDevice>("CP-CLK");
        var connectedAt = await device.ReadServerNowAsync();
        Proto.Context.Clock.Advance(TimeSpan.FromHours(2));
        var advancedAt = await device.ReadServerNowAsync();

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(
                connectedAt,
                Is.EqualTo(seed),
                "the handshake links the socket to the test that opened it");
            Assert.That(
                advancedAt,
                Is.EqualTo(seed.AddHours(2)),
                "advancing the test clock reaches the application over the socket");
        });
    }

    [Test]
    public async Task InProcessConnect_FromAFlowWithoutAmbientContext_ShouldResolveTheRunClock()
    {
        var seed = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);
        await using var host = ClockDeviceApplication(seed).Build();
        await host.StartAsync();
        await host.StartTestAsync("in-process device run clock", "00001", TestMethods.Placeholder);

        var device = Proto.Context.Devices("Chargers").For<ClockDevice>("CP-RUN");
        Proto.Context.Clock.Advance(TimeSpan.FromHours(2));
        DateTimeOffset serverNow;
        // The connect opens on a flow with no test context, so the handshake carries no test id and the
        // application resolves the run clock; the connect itself must still reach the application.
        using (ExecutionContext.SuppressFlow())
        {
            serverNow = Task.Run(() => device.ReadServerNowAsync().AsTask()).GetAwaiter().GetResult();
        }

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.That(
            serverNow,
            Is.EqualTo(seed),
            "a handshake with no test on its flow keeps the run-clock fallback");
    }

    private static ProtoHostBuilder ClockDeviceApplication(DateTimeOffset seed)
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureClock(new ProtoClock(seed));
        builder.ConfigureTracing(options => options.Enabled = false);
        builder
            .AddInProcessWebSocketDevices<SampleApi.Program>("Api")
            .AddApplication("Api", app => app
                .AddAspNetCoreServer<SampleApi.Program>()
                .AddDevices(devices => devices
                    .AddWebSocketClient("Chargers", path: "/ws/{deviceId}")
                        .AddDevice<ClockDevice>()));
        return builder;
    }

    private sealed class ClockDevice : ProtoDevice
    {
        public async ValueTask<DateTimeOffset> ReadServerNowAsync()
        {
            await SendTextAsync("NOW");
            var frame = await ExpectAsync(
                "server sends its TimeProvider value",
                candidate => candidate.TryGetText(out var text)
                    && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _),
                TimeSpan.FromSeconds(5));
            return DateTimeOffset.Parse(frame.AsText(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }
    }
}
