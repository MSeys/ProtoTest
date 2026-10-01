namespace ProtoTest.Devices;

using System.Buffers;
using ProtoTest.Devices.Exceptions;

/// <summary>
/// A device connection over a byte stream, framed by an <see cref="IDeviceFramer"/>. The TCP and serial
/// transports use it, and a hand-written transport over any other stream (a Bluetooth socket, a USB
/// bridge) can wrap its stream the same way.
/// </summary>
public sealed class StreamDeviceConnection : IProtoDeviceConnection
{
    /// <summary>The largest frame a connection buffers by default: 1 MiB.</summary>
    public const int DefaultMaxFrameBytes = 1024 * 1024;

    private const int ReadChunkBytes = 4096;

    private readonly Stream _stream;
    private readonly IDeviceFramer _framer;
    private readonly int _maxFrameBytes;
    private readonly IAsyncDisposable? _owner;
    private byte[] _buffer = new byte[ReadChunkBytes];
    private int _count;

    /// <summary>Wraps <paramref name="stream"/>; disposing the connection disposes the stream and <paramref name="owner"/>.</summary>
    /// <param name="stream">The open stream to the device.</param>
    /// <param name="framer">How the stream's bytes become frames.</param>
    /// <param name="remoteAddress">The address the stream reached, for errors and the trace.</param>
    /// <param name="maxFrameBytes">The most bytes a receive buffers before it fails without a whole frame.</param>
    /// <param name="owner">What else to dispose with the stream, such as its socket or port.</param>
    public StreamDeviceConnection(
        Stream stream,
        IDeviceFramer framer,
        string? remoteAddress = null,
        int maxFrameBytes = DefaultMaxFrameBytes,
        IAsyncDisposable? owner = null)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _framer = framer ?? throw new ArgumentNullException(nameof(framer));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFrameBytes);
        _maxFrameBytes = maxFrameBytes;
        RemoteAddress = remoteAddress;
        _owner = owner;
    }

    /// <inheritdoc />
    public string? RemoteAddress { get; }

    /// <inheritdoc />
    public async ValueTask SendAsync(DeviceFrame frame, CancellationToken cancellationToken = default)
    {
        var output = new ArrayBufferWriter<byte>();
        _framer.Write(frame, output);
        await _stream.WriteAsync(output.WrittenMemory, cancellationToken).ConfigureAwait(false);
        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<DeviceFrame?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            if (_count > 0)
            {
                var found = _framer.TryRead(_buffer.AsSpan(0, _count), out var frame, out var consumed);
                Drop(consumed);
                if (found)
                {
                    return frame;
                }
            }

            if (_count > _maxFrameBytes)
            {
                throw new DeviceFramingException(
                    $"The device at '{Describe()}' sent {_count} bytes without completing a frame, more than the " +
                    $"{_maxFrameBytes} a receive buffers. First bytes: {Preview()}. Check the framer, or raise the limit.");
            }

            if (_count == _buffer.Length)
            {
                Array.Resize(ref _buffer, Math.Min(_buffer.Length * 2, _maxFrameBytes + ReadChunkBytes));
            }

            var read = await _stream.ReadAsync(_buffer.AsMemory(_count), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                if (_count == 0)
                {
                    return null;
                }

                throw new DeviceFramingException(
                    $"The device at '{Describe()}' closed the connection in the middle of a frame: {_count} bytes " +
                    $"never completed one. First bytes: {Preview()}.");
            }

            _count += read;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _stream.DisposeAsync().ConfigureAwait(false);
        if (_owner is not null)
        {
            await _owner.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void Drop(int consumed)
    {
        if (consumed <= 0)
        {
            return;
        }

        consumed = Math.Min(consumed, _count);
        Buffer.BlockCopy(_buffer, consumed, _buffer, 0, _count - consumed);
        _count -= consumed;
    }

    private string Describe() => RemoteAddress ?? "unknown address";

    private string Preview()
    {
        var length = Math.Min(_count, 32);
        var hex = Convert.ToHexString(_buffer, 0, length);
        return _count > length ? $"{hex}..." : hex;
    }
}

/// <summary>
/// A device connection that waits for the device to connect to the test, as a listening TCP transport
/// does. <see cref="ListenAddress"/> is the address to hand the system under test.
/// </summary>
public interface IProtoDeviceListener
{
    /// <summary>Gets the address the test listens on, with the port the operating system chose.</summary>
    string ListenAddress { get; }
}
