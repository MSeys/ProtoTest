namespace ProtoTest.Devices.Tests;

using ProtoTest.Core;

/// <summary>
/// Pins the device session's check-then-act connect (audit Stage A0): two concurrent sends each pass
/// the "not connected yet" check and open their own connection, one of which is never disposed.
/// </summary>
[TestFixture]
[Category("Characterization")]
public sealed class DeviceConnectConcurrencyCharacterizationTests
{
    [Test]
    public async Task TwoConcurrentSends_ShouldOpenTwoConnectionsBeforeEitherCompletes()
    {
        var transport = new GatedTransport();
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddDevices(devices => devices
            .AddClient("Chargers", transport, resolveAddress: ProtoDeviceAddress.Template("memory://cp-001"))
                .AddDevice<ConcurrentDevice>());
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("concurrent device sends", "00001", TestMethods.Placeholder);

        var device = Proto.Context.Devices("Chargers").For<ConcurrentDevice>("CP-001");
        var first = device.SendTextAsync("A");
        var second = device.SendTextAsync("B");

        Assert.Multiple(() =>
        {
            // Pins current behavior; audit DEV-3 flips this once connect is single-flight per session:
            // the second send passes the '_connection is null' check while the first is still awaiting
            // the transport, so both enter ConnectAsync before either can complete.
            Assert.That(transport.ConnectCount, Is.EqualTo(2), "both sends entered the transport's connect");
            Assert.That(first.IsCompleted, Is.False, "the transport gate is still closed");
            Assert.That(second.IsCompleted, Is.False, "the transport gate is still closed");
        });

        transport.Release();
        await first;
        await second;

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        // Pins current behavior; audit DEV-3 flips this once concurrent sends share one connection.
        Assert.That(
            transport.DisposedConnectionCount,
            Is.EqualTo(1),
            "the session keeps only the last connection written, so the other is orphaned and never disposed");
    }

    private sealed class ConcurrentDevice : ProtoDevice
    {
        public ValueTask SendTextAsync(string text) => SendAsync(DeviceFrame.Text(text));
    }

    private sealed class GatedTransport : IProtoDeviceTransport
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _connectCount;
        private int _disposedConnectionCount;

        public string Name => "Gated";

        public int ConnectCount => Volatile.Read(ref _connectCount);

        public int DisposedConnectionCount => Volatile.Read(ref _disposedConnectionCount);

        public async ValueTask<IProtoDeviceConnection> ConnectAsync(
            DeviceEndpoint endpoint,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _connectCount);
            await _gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new GatedConnection(() => Interlocked.Increment(ref _disposedConnectionCount));
        }

        public void Release() => _gate.TrySetResult();
    }

    private sealed class GatedConnection(Action onDispose) : IProtoDeviceConnection
    {
        public string? RemoteAddress => "memory://gated";

        public ValueTask SendAsync(DeviceFrame frame, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask<DeviceFrame?> ReceiveAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult<DeviceFrame?>(null);

        public ValueTask DisposeAsync()
        {
            onDispose();
            return ValueTask.CompletedTask;
        }
    }
}
