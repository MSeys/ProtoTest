namespace ProtoTest.Devices.Tests;

using System.Diagnostics;
using ProtoTest.Core;

/// <summary>
/// The test's release must terminate even when a transport's connect never
/// completes; the abandoned connect is recorded instead of hanging the teardown forever.
/// </summary>
[TestFixture]
public sealed class DeviceReleaseBoundTests
{
    [Test]
    public async Task Release_ShouldBoundANeverConnectingTransportAndRecordIt()
    {
        var transport = new NeverConnectingTransport();
        var builder = new ProtoHostBuilder();
        builder.AddDevices(devices => devices
            .AddClient("Chargers", transport, resolveAddress: ProtoDeviceAddress.Template("memory://cp-001"))
                .AddDevice<WaitDevice>());
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("never connecting device", "00001", TestMethods.Placeholder);

        var device = Proto.Context.Devices("Chargers").For<WaitDevice>("CP-001");
        var pending = device.WaitFrameAsync().AsTask();
        await transport.ConnectEntered.WaitAsync(TimeSpan.FromSeconds(5));

        var stopwatch = Stopwatch.StartNew();
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        stopwatch.Stop();

        var entry = host.Trace.Snapshot().Tests.Single().Entries
            .Single(candidate => candidate.Kind == "device.disconnect.abandoned");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(30)),
                "release completes despite the hung connect");
            Assert.That(pending.IsCompleted, Is.False, "the hung connect is not adopted by the release");
            Assert.That(entry.Outcome, Is.EqualTo(ProtoTraceOutcome.Failed));
            Assert.That(entry.Attributes["device.id"], Is.EqualTo("CP-001"));
        }
    }

    private sealed class WaitDevice : ProtoDevice
    {
        public ValueTask<DeviceFrame> WaitFrameAsync() => ReceiveAsync();
    }

    private sealed class NeverConnectingTransport : IProtoDeviceTransport
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ConnectEntered => _entered.Task;

        public string Name => "Never";

        public async ValueTask<IProtoDeviceConnection> ConnectAsync(
            DeviceEndpoint endpoint,
            CancellationToken cancellationToken = default)
        {
            _entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("unreachable");
        }
    }
}
