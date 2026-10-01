namespace ProtoTest.Devices;

using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using ProtoTest.Devices.Exceptions;

/// <summary>
/// Cuts a byte stream into device frames and writes frames back. A stream transport (TCP, serial) has no
/// message boundaries of its own, so the framer is the one piece of protocol knowledge it carries; what a
/// frame means stays in the device class.
/// </summary>
public interface IDeviceFramer
{
    /// <summary>
    /// Reads one frame from the start of <paramref name="buffered"/>. Returns false while the bytes do not
    /// hold a whole frame yet. <paramref name="consumed"/> is how many bytes to drop from the front in
    /// either case: the frame and its delimiters, or noise a framer skips before a frame starts.
    /// </summary>
    bool TryRead(ReadOnlySpan<byte> buffered, out DeviceFrame frame, out int consumed);

    /// <summary>Writes one frame with the delimiters or header the protocol needs.</summary>
    void Write(DeviceFrame frame, IBufferWriter<byte> output);
}

/// <summary>The framers most device protocols need. A protocol with its own envelope implements <see cref="IDeviceFramer"/>.</summary>
public static class DeviceFramers
{
    /// <summary>
    /// Text lines: each frame is the text before <paramref name="terminator"/>, read and written as UTF-8.
    /// Use <c>"\r\n"</c> for CRLF protocols. The terminator is not part of the frame.
    /// </summary>
    public static IDeviceFramer Lines(string terminator = "\n")
    {
        ArgumentException.ThrowIfNullOrEmpty(terminator);
        return new DelimitedFramer(Encoding.UTF8.GetBytes(terminator), "text/plain");
    }

    /// <summary>Frames ended by <paramref name="delimiter"/>, which is not part of the frame.</summary>
    public static IDeviceFramer Delimited(ReadOnlyMemory<byte> delimiter, string mediaType = "application/octet-stream")
    {
        if (delimiter.IsEmpty)
        {
            throw new ArgumentException("The delimiter needs at least one byte.", nameof(delimiter));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        return new DelimitedFramer(delimiter.ToArray(), mediaType);
    }

    /// <summary>
    /// Frames wrapped in a start and an end marker, such as STX (0x02) and ETX (0x03). Bytes before a start
    /// marker are skipped as line noise; the markers are not part of the frame.
    /// </summary>
    public static IDeviceFramer Enveloped(
        ReadOnlyMemory<byte> start,
        ReadOnlyMemory<byte> end,
        string mediaType = "application/octet-stream")
    {
        if (start.IsEmpty || end.IsEmpty)
        {
            throw new ArgumentException("The start and end markers need at least one byte each.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        return new EnvelopedFramer(start.ToArray(), end.ToArray(), mediaType);
    }

    /// <summary>
    /// Frames preceded by their payload length in a 1, 2 or 4 byte unsigned header, big-endian unless
    /// <paramref name="bigEndian"/> is false. The header is not part of the frame.
    /// </summary>
    public static IDeviceFramer LengthPrefixed(
        int headerBytes = 4,
        bool bigEndian = true,
        string mediaType = "application/octet-stream")
    {
        if (headerBytes is not (1 or 2 or 4))
        {
            throw new ArgumentOutOfRangeException(nameof(headerBytes), headerBytes, "The length header is 1, 2 or 4 bytes.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        return new LengthPrefixedFramer(headerBytes, bigEndian, mediaType);
    }

    /// <summary>Frames of exactly <paramref name="bytes"/> bytes each.</summary>
    public static IDeviceFramer FixedLength(int bytes, string mediaType = "application/octet-stream")
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        return new FixedLengthFramer(bytes, mediaType);
    }

    private sealed class DelimitedFramer(byte[] delimiter, string mediaType) : IDeviceFramer
    {
        public bool TryRead(ReadOnlySpan<byte> buffered, out DeviceFrame frame, out int consumed)
        {
            var end = buffered.IndexOf(delimiter);
            if (end < 0)
            {
                frame = default;
                consumed = 0;
                return false;
            }

            frame = new DeviceFrame(buffered[..end].ToArray(), mediaType);
            consumed = end + delimiter.Length;
            return true;
        }

        public void Write(DeviceFrame frame, IBufferWriter<byte> output)
        {
            ArgumentNullException.ThrowIfNull(output);
            if (frame.Payload.Span.IndexOf(delimiter) >= 0)
            {
                throw new ArgumentException(
                    $"The frame '{frame}' contains its own delimiter, so the device would read it as two frames.",
                    nameof(frame));
            }

            output.Write(frame.Payload.Span);
            output.Write(delimiter);
        }
    }

    private sealed class EnvelopedFramer(byte[] start, byte[] end, string mediaType) : IDeviceFramer
    {
        public bool TryRead(ReadOnlySpan<byte> buffered, out DeviceFrame frame, out int consumed)
        {
            frame = default;
            var startAt = buffered.IndexOf(start);
            if (startAt < 0)
            {
                // Nothing that can start a frame: keep a tail that could be the start of a split marker.
                consumed = Math.Max(0, buffered.Length - (start.Length - 1));
                return false;
            }

            var body = buffered[(startAt + start.Length)..];
            var endAt = body.IndexOf(end);
            if (endAt < 0)
            {
                consumed = startAt;
                return false;
            }

            frame = new DeviceFrame(body[..endAt].ToArray(), mediaType);
            consumed = startAt + start.Length + endAt + end.Length;
            return true;
        }

        public void Write(DeviceFrame frame, IBufferWriter<byte> output)
        {
            ArgumentNullException.ThrowIfNull(output);
            if (frame.Payload.Span.IndexOf(end) >= 0)
            {
                throw new ArgumentException(
                    $"The frame '{frame}' contains its own end marker, so the device would read it cut short.",
                    nameof(frame));
            }

            output.Write(start);
            output.Write(frame.Payload.Span);
            output.Write(end);
        }
    }

    private sealed class LengthPrefixedFramer(int headerBytes, bool bigEndian, string mediaType) : IDeviceFramer
    {
        private long MaxLength => headerBytes switch { 1 => byte.MaxValue, 2 => ushort.MaxValue, _ => int.MaxValue };

        public bool TryRead(ReadOnlySpan<byte> buffered, out DeviceFrame frame, out int consumed)
        {
            frame = default;
            consumed = 0;
            if (buffered.Length < headerBytes)
            {
                return false;
            }

            var header = buffered[..headerBytes];
            long length = headerBytes switch
            {
                1 => header[0],
                2 => bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(header) : BinaryPrimitives.ReadUInt16LittleEndian(header),
                _ => bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(header) : BinaryPrimitives.ReadUInt32LittleEndian(header)
            };
            if (length > int.MaxValue - headerBytes)
            {
                throw new DeviceFramingException($"The length header announces {length} bytes, more than a frame can hold.");
            }

            if (buffered.Length < headerBytes + length)
            {
                return false;
            }

            frame = new DeviceFrame(buffered.Slice(headerBytes, (int)length).ToArray(), mediaType);
            consumed = headerBytes + (int)length;
            return true;
        }

        public void Write(DeviceFrame frame, IBufferWriter<byte> output)
        {
            ArgumentNullException.ThrowIfNull(output);
            var length = frame.Payload.Length;
            if (length > MaxLength)
            {
                throw new ArgumentException(
                    $"The frame is {length} bytes, more than a {headerBytes}-byte length header can announce ({MaxLength}).",
                    nameof(frame));
            }

            var header = output.GetSpan(headerBytes)[..headerBytes];
            switch (headerBytes)
            {
                case 1:
                    header[0] = (byte)length;
                    break;
                case 2 when bigEndian:
                    BinaryPrimitives.WriteUInt16BigEndian(header, (ushort)length);
                    break;
                case 2:
                    BinaryPrimitives.WriteUInt16LittleEndian(header, (ushort)length);
                    break;
                default:
                    if (bigEndian)
                    {
                        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)length);
                    }
                    else
                    {
                        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)length);
                    }

                    break;
            }

            output.Advance(headerBytes);
            output.Write(frame.Payload.Span);
        }
    }

    private sealed class FixedLengthFramer(int bytes, string mediaType) : IDeviceFramer
    {
        public bool TryRead(ReadOnlySpan<byte> buffered, out DeviceFrame frame, out int consumed)
        {
            if (buffered.Length < bytes)
            {
                frame = default;
                consumed = 0;
                return false;
            }

            frame = new DeviceFrame(buffered[..bytes].ToArray(), mediaType);
            consumed = bytes;
            return true;
        }

        public void Write(DeviceFrame frame, IBufferWriter<byte> output)
        {
            ArgumentNullException.ThrowIfNull(output);
            if (frame.Payload.Length != bytes)
            {
                throw new ArgumentException(
                    $"The frame is {frame.Payload.Length} bytes; this protocol's frames are exactly {bytes}.",
                    nameof(frame));
            }

            output.Write(frame.Payload.Span);
        }
    }
}
