namespace ProtoTest.Devices.Tests;

using System.Buffers;
using System.Text;
using ProtoTest.Core;
using ProtoTest.Devices.Exceptions;

[TestFixture]
public sealed class DeviceFramingTests
{
    [Test]
    public void Lines_ShouldReadEachLineWithoutItsTerminatorAndWaitForAPartialOne()
    {
        var framer = DeviceFramers.Lines("\r\n");
        var bytes = Encoding.UTF8.GetBytes("$MTR,1\r\n$MTR,2\r\n$MT");

        Assert.Multiple(() =>
        {
            Assert.That(framer.TryRead(bytes, out var first, out var consumed), Is.True);
            Assert.That(first.AsText(), Is.EqualTo("$MTR,1"));
            Assert.That(consumed, Is.EqualTo(8));
            Assert.That(framer.TryRead(bytes.AsSpan(8), out var second, out _), Is.True);
            Assert.That(second.AsText(), Is.EqualTo("$MTR,2"));
            Assert.That(framer.TryRead(bytes.AsSpan(16), out _, out var none), Is.False, "a line without its terminator waits");
            Assert.That(none, Is.Zero);
        });
    }

    [Test]
    public void Lines_ShouldRefuseToWriteAFrameThatContainsItsTerminator()
    {
        var framer = DeviceFramers.Lines();

        Assert.That(
            () => framer.Write(DeviceFrame.Text("one\ntwo"), new ArrayBufferWriter<byte>()),
            Throws.ArgumentException.With.Message.Contains("contains its own delimiter"));
    }

    [Test]
    public void Enveloped_ShouldSkipNoiseBeforeTheStartMarker()
    {
        var framer = DeviceFramers.Enveloped(new byte[] { 0x02 }, new byte[] { 0x03 });
        byte[] bytes = [0xFF, 0x00, 0x02, (byte)'O', (byte)'K', 0x03];

        Assert.Multiple(() =>
        {
            Assert.That(framer.TryRead(bytes, out var frame, out var consumed), Is.True);
            Assert.That(Encoding.ASCII.GetString(frame.Payload.Span), Is.EqualTo("OK"));
            Assert.That(consumed, Is.EqualTo(bytes.Length));
            Assert.That(framer.TryRead(new byte[] { 0xFF, 0xFF }, out _, out var noise), Is.False);
            Assert.That(noise, Is.EqualTo(2), "noise without a start marker is dropped");
        });
    }

    [TestCase(1, true)]
    [TestCase(2, true)]
    [TestCase(2, false)]
    [TestCase(4, true)]
    [TestCase(4, false)]
    public void LengthPrefixed_ShouldRoundTripAPayload(int headerBytes, bool bigEndian)
    {
        var framer = DeviceFramers.LengthPrefixed(headerBytes, bigEndian);
        var output = new ArrayBufferWriter<byte>();
        framer.Write(DeviceFrame.Binary(new byte[] { 1, 2, 3 }), output);
        var written = output.WrittenSpan.ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(written, Has.Length.EqualTo(headerBytes + 3));
            Assert.That(framer.TryRead(written.AsSpan(0, written.Length - 1), out _, out _), Is.False, "a short payload waits");
            Assert.That(framer.TryRead(written, out var frame, out var consumed), Is.True);
            Assert.That(frame.Payload.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(consumed, Is.EqualTo(written.Length));
        });
    }

    [Test]
    public void LengthPrefixed_ShouldRefuseAPayloadItsHeaderCannotAnnounce()
        => Assert.That(
            () => DeviceFramers.LengthPrefixed(1).Write(DeviceFrame.Binary(new byte[300]), new ArrayBufferWriter<byte>()),
            Throws.ArgumentException.With.Message.Contains("1-byte length header"));

    [Test]
    public void FixedLength_ShouldReadExactlyItsSize()
    {
        var framer = DeviceFramers.FixedLength(4);

        Assert.Multiple(() =>
        {
            Assert.That(framer.TryRead(new byte[] { 1, 2, 3 }, out _, out _), Is.False);
            Assert.That(framer.TryRead(new byte[] { 1, 2, 3, 4, 5 }, out var frame, out var consumed), Is.True);
            Assert.That(frame.Payload.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
            Assert.That(consumed, Is.EqualTo(4));
            Assert.That(
                () => framer.Write(DeviceFrame.Binary(new byte[3]), new ArrayBufferWriter<byte>()),
                Throws.ArgumentException.With.Message.Contains("exactly 4"));
        });
    }

    [Test]
    public async Task StreamConnection_ShouldAssembleFramesSplitAcrossReadsAndSeveralInOneRead()
    {
        var stream = new ScriptedStream("$A,1\n$B", ",2\n$C,3\n");
        await using var connection = new StreamDeviceConnection(stream, DeviceFramers.Lines(), "tcp://meter");

        var frames = new List<string>();
        for (var index = 0; index < 3; index++)
        {
            frames.Add((await connection.ReceiveAsync())!.Value.AsText());
        }

        Assert.Multiple(async () =>
        {
            Assert.That(frames, Is.EqualTo(new[] { "$A,1", "$B,2", "$C,3" }));
            Assert.That(await connection.ReceiveAsync(), Is.Null, "a close at a frame boundary ends the exchange");
        });
    }

    [Test]
    public async Task StreamConnection_ShouldNameTheAddressWhenTheDeviceClosesMidFrame()
    {
        await using var connection = new StreamDeviceConnection(new ScriptedStream("$A,1\n$B,"), DeviceFramers.Lines(), "tcp://meter");
        await connection.ReceiveAsync();

        var exception = Assert.ThrowsAsync<DeviceFramingException>(async () => await connection.ReceiveAsync());

        Assert.That(exception!.Message, Does.Contain("tcp://meter").And.Contain("in the middle of a frame").And.Contain("3 bytes"));
    }

    [Test]
    public async Task StreamConnection_ShouldFailAFrameThatOutgrowsTheLimit()
    {
        await using var connection = new StreamDeviceConnection(
            new ScriptedStream(new string('x', 64)),
            DeviceFramers.Lines(),
            "tcp://meter",
            maxFrameBytes: 16);

        var exception = Assert.ThrowsAsync<DeviceFramingException>(async () => await connection.ReceiveAsync());

        Assert.That(exception!.Message, Does.Contain("without completing a frame").And.Contain("16"));
    }

    [Test]
    public async Task StreamConnection_ShouldWriteTheFramedBytes()
    {
        var stream = new ScriptedStream();
        await using var connection = new StreamDeviceConnection(stream, DeviceFramers.Lines("\r\n"));

        await connection.SendAsync(DeviceFrame.Text("AT+GMR"));

        Assert.That(Encoding.ASCII.GetString(stream.Written.ToArray()), Is.EqualTo("AT+GMR\r\n"));
    }

    [Test]
    public async Task WithFramer_ShouldReachTheEndpointAndListen_ShouldNameAConnectingTransport()
    {
        var transport = new CapturingTransport();
        var framer = DeviceFramers.Lines("\r\n");
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddDevices(devices => devices
            .AddClient("Meters", transport, resolveAddress: ProtoDeviceAddress.Template("memory://{deviceId}"))
            .WithFramer(framer)
            .AddDevice<ListeningMeter>());
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("framer", "00001", TestMethods.Placeholder);

        var meter = Proto.Context.Devices("Meters").For<ListeningMeter>("M-1");
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await meter.OpenPortAsync());

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
        Assert.Multiple(() =>
        {
            Assert.That(transport.LastEndpoint?.Framer, Is.SameAs(framer));
            Assert.That(exception!.Message, Does.Contain("connects out over Capturing").And.Contain("AddTcpListener"));
        });
    }

    private sealed class ListeningMeter : ProtoDevice
    {
        public ValueTask<string> OpenPortAsync() => ListenAsync();
    }

    private sealed class CapturingTransport : IProtoDeviceTransport
    {
        public string Name => "Capturing";

        public DeviceEndpoint? LastEndpoint { get; private set; }

        public ValueTask<IProtoDeviceConnection> ConnectAsync(DeviceEndpoint endpoint, CancellationToken cancellationToken = default)
        {
            LastEndpoint = endpoint;
            return ValueTask.FromResult<IProtoDeviceConnection>(new StreamDeviceConnection(new ScriptedStream(), endpoint.Framer!));
        }
    }

    /// <summary>Returns one scripted chunk per read, then end of stream; records what was written.</summary>
    private sealed class ScriptedStream(params string[] chunks) : Stream
    {
        private readonly Queue<byte[]> _chunks = new(chunks.Select(Encoding.UTF8.GetBytes));

        public MemoryStream Written { get; } = new();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (!_chunks.TryPeek(out var chunk))
            {
                return 0;
            }

            var length = Math.Min(count, chunk.Length);
            Array.Copy(chunk, 0, buffer, offset, length);
            _chunks.Dequeue();
            if (length < chunk.Length)
            {
                _chunks.Enqueue(chunk[length..]);
            }

            return length;
        }

        public override void Write(byte[] buffer, int offset, int count) => Written.Write(buffer, offset, count);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
