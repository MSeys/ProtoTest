namespace ProtoTest.Devices.Tests;

using ProtoTest.Core;

/// <summary>
/// Pins the device session's concurrency contract (audit DEV-3): connect is single-flight, sends are
/// serialized, one receive is in flight at a time, and a disconnect that races a send fails with a
/// device error naming the device instead of a disposed-socket exception.
/// </summary>
[TestFixture]
[Category("Characterization")]
public sealed class DeviceConnectConcurrencyCharacterizationTests
{
    [Test]
    public async Task TwoConcurrentSends_ShouldShareOneConnectionAndComplete()
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
            Assert.That(
                transport.ConnectCount,
                Is.EqualTo(1),
                "connect is single-flight: the second send waits for the first instead of opening another connection");
            Assert.That(first.IsCompleted, Is.False, "the transport gate is still closed");
            Assert.That(second.IsCompleted, Is.False, "the transport gate is still closed");
        });

        transport.Release();
        await first;
        await second;

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.That(
            transport.DisposedConnectionCount,
            Is.EqualTo(1),
            "the one shared connection is disposed by the release path; no connection is orphaned");
    }

    [Test]
    public async Task DisconnectRacingSend_ShouldFailWithTheNamedDeviceError()
    {
        var transport = new RacingTransport();
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddDevices(devices => devices
            .AddClient("Chargers", transport, resolveAddress: ProtoDeviceAddress.Template("memory://cp-001"))
                .AddDevice<ConcurrentDevice>());
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("disconnect racing send", "00001", TestMethods.Placeholder);

        var device = Proto.Context.Devices("Chargers").For<ConcurrentDevice>("CP-001");
        var send = device.SendTextAsync("A").AsTask();
        await transport.Connection.SendEntered.WaitAsync(TimeSpan.FromSeconds(5));

        await device.CloseAsync();
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await send);

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("'CP-001'"), "the error names the device");
            Assert.That(exception.Message, Does.Contain("disconnected while sending"));
            Assert.That(
                exception.InnerException,
                Is.InstanceOf<ObjectDisposedException>(),
                "the transport's failure stays as the inner exception; the caller sees a device error, not an NRE");
        });
    }

    [Test]
    public async Task ConcurrentReceives_ShouldFailFastWithTheNamedDeviceError()
    {
        var transport = new BlockingReceiveTransport();
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddDevices(devices => devices
            .AddClient("Chargers", transport, resolveAddress: ProtoDeviceAddress.Template("memory://cp-001"))
                .AddDevice<ConcurrentDevice>());
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("concurrent device receives", "00001", TestMethods.Placeholder);

        var device = Proto.Context.Devices("Chargers").For<ConcurrentDevice>("CP-001");
        var first = device.WaitFrameAsync().AsTask();
        await transport.Connection.ReceiveEntered.WaitAsync(TimeSpan.FromSeconds(5));

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await device.WaitFrameAsync());

        transport.Connection.ReleaseReceive(DeviceFrame.Text("FRAME"));
        var frame = await first;

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(frame.AsText(), Is.EqualTo("FRAME"), "the first reader keeps its frame");
            Assert.That(exception!.Message, Does.Contain("'CP-001'"), "the error names the device");
            Assert.That(exception.Message, Does.Contain("one frame at a time"));
        });
    }

    private sealed class ConcurrentDevice : ProtoDevice
    {
        public ValueTask SendTextAsync(string text) => SendAsync(DeviceFrame.Text(text));

        public ValueTask CloseAsync() => DisconnectAsync();

        public ValueTask<DeviceFrame> WaitFrameAsync() => ReceiveAsync();
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

    private sealed class RacingTransport : IProtoDeviceTransport
    {
        public RacingConnection Connection { get; } = new();

        public string Name => "Racing";

        public ValueTask<IProtoDeviceConnection> ConnectAsync(
            DeviceEndpoint endpoint,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IProtoDeviceConnection>(Connection);
    }

    private sealed class RacingConnection : IProtoDeviceConnection
    {
        private readonly TaskCompletionSource _sendEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseSend = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task SendEntered => _sendEntered.Task;

        public string? RemoteAddress => "memory://racing";

        public async ValueTask SendAsync(DeviceFrame frame, CancellationToken cancellationToken = default)
        {
            _sendEntered.TrySetResult();
            await _releaseSend.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            throw new ObjectDisposedException(nameof(RacingConnection), "The connection was disposed while sending.");
        }

        public ValueTask<DeviceFrame?> ReceiveAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult<DeviceFrame?>(null);

        public ValueTask DisposeAsync()
        {
            _releaseSend.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingReceiveTransport : IProtoDeviceTransport
    {
        public BlockingReceiveConnection Connection { get; } = new();

        public string Name => "Blocking";

        public ValueTask<IProtoDeviceConnection> ConnectAsync(
            DeviceEndpoint endpoint,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IProtoDeviceConnection>(Connection);
    }

    private sealed class BlockingReceiveConnection : IProtoDeviceConnection
    {
        private readonly TaskCompletionSource _receiveEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<DeviceFrame> _releaseReceive = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ReceiveEntered => _receiveEntered.Task;

        public string? RemoteAddress => "memory://blocking";

        public ValueTask SendAsync(DeviceFrame frame, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public async ValueTask<DeviceFrame?> ReceiveAsync(CancellationToken cancellationToken = default)
        {
            _receiveEntered.TrySetResult();
            return await _releaseReceive.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        public void ReleaseReceive(DeviceFrame frame) => _releaseReceive.TrySetResult(frame);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
