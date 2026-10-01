namespace ProtoTest.Devices;

using System.Buffers.Binary;
using System.Globalization;
using System.Text;

/// <summary>Converts one text field of type <typeparamref name="T"/> to and from its text in the message.</summary>
public interface IDeviceFieldFormatter<T>
{
    /// <summary>Writes the value as the device sends it.</summary>
    string Format(T value);

    /// <summary>Reads the device's text; throws <see cref="FormatException"/> when it is not a value.</summary>
    T Parse(string text);
}

/// <summary>Converts one binary field of type <typeparamref name="T"/> to and from a fixed number of bytes.</summary>
public interface IDeviceBinaryFieldFormatter<T>
{
    /// <summary>Gets how many bytes the field takes.</summary>
    int Size { get; }

    /// <summary>Writes the value into exactly <see cref="Size"/> bytes.</summary>
    void Write(T value, Span<byte> destination, bool bigEndian);

    /// <summary>Reads the value from exactly <see cref="Size"/> bytes.</summary>
    T Read(ReadOnlySpan<byte> source, bool bigEndian);
}

/// <summary>A checksum a text message carries after its body, such as NMEA's <c>*hh</c>.</summary>
public interface IDeviceMessageChecksum
{
    /// <summary>Gets the text between the body and the checksum, such as <c>*</c>.</summary>
    string Separator { get; }

    /// <summary>Computes the checksum text for the message body (everything before the separator).</summary>
    string Compute(string body);
}

/// <summary>A checksum a binary message carries after its body, such as Modbus RTU's CRC-16.</summary>
public interface IDeviceBinaryChecksum
{
    /// <summary>Gets how many bytes the checksum takes.</summary>
    int Size { get; }

    /// <summary>Computes the checksum of <paramref name="body"/> (every byte before it) into <paramref name="destination"/>.</summary>
    void Compute(ReadOnlySpan<byte> body, Span<byte> destination);
}

/// <summary>
/// The NMEA 0183 checksum: <c>*</c> and two uppercase hex digits, the XOR of every character after a
/// leading <c>$</c> or <c>!</c>.
/// </summary>
public sealed class NmeaChecksum : IDeviceMessageChecksum
{
    /// <inheritdoc />
    public string Separator => "*";

    /// <inheritdoc />
    public string Compute(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var start = body.Length > 0 && body[0] is '$' or '!' ? 1 : 0;
        byte sum = 0;
        foreach (var character in Encoding.ASCII.GetBytes(body[start..]))
        {
            sum ^= character;
        }

        return sum.ToString("X2", CultureInfo.InvariantCulture);
    }
}

/// <summary>The Modbus RTU CRC-16 (polynomial 0xA001, initial 0xFFFF), appended low byte first.</summary>
public sealed class Crc16Modbus : IDeviceBinaryChecksum
{
    /// <inheritdoc />
    public int Size => 2;

    /// <inheritdoc />
    public void Compute(ReadOnlySpan<byte> body, Span<byte> destination)
    {
        ushort crc = 0xFFFF;
        foreach (var value in body)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
            }
        }

        BinaryPrimitives.WriteUInt16LittleEndian(destination, crc);
    }
}

/// <summary>A one-byte XOR of every body byte.</summary>
public sealed class Xor8Checksum : IDeviceBinaryChecksum
{
    /// <inheritdoc />
    public int Size => 1;

    /// <inheritdoc />
    public void Compute(ReadOnlySpan<byte> body, Span<byte> destination)
    {
        byte sum = 0;
        foreach (var value in body)
        {
            sum ^= value;
        }

        destination[0] = sum;
    }
}

/// <summary>A one-byte sum of every body byte, modulo 256.</summary>
public sealed class Sum8Checksum : IDeviceBinaryChecksum
{
    /// <inheritdoc />
    public int Size => 1;

    /// <inheritdoc />
    public void Compute(ReadOnlySpan<byte> body, Span<byte> destination)
    {
        byte sum = 0;
        foreach (var value in body)
        {
            sum = unchecked((byte)(sum + value));
        }

        destination[0] = sum;
    }
}

/// <summary>
/// Formatters for values devices encode in their own way. Use one with
/// <c>[DeviceFormat&lt;DeviceFormatters.Hundredths&gt;]</c>, or derive from a base here for another scale.
/// </summary>
public static class DeviceFormatters
{
    /// <summary>A decimal sent as a whole number scaled by 10^decimals: with 2, <c>12.50</c> is <c>1250</c>.</summary>
    public abstract class ScaledDecimal(int decimals) : IDeviceFieldFormatter<decimal>
    {
        private readonly decimal _factor = Factor(decimals);

        /// <inheritdoc />
        public string Format(decimal value) => decimal.Round(value * _factor).ToString("0", CultureInfo.InvariantCulture);

        /// <inheritdoc />
        public decimal Parse(string text) => long.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) / _factor;
    }

    /// <summary>One decimal place as a whole number: <c>12.5</c> is <c>125</c>.</summary>
    public sealed class Tenths() : ScaledDecimal(1);

    /// <summary>Two decimal places as a whole number: <c>12.50</c> is <c>1250</c>.</summary>
    public sealed class Hundredths() : ScaledDecimal(2);

    /// <summary>Three decimal places as a whole number: <c>12.500</c> is <c>12500</c>.</summary>
    public sealed class Thousandths() : ScaledDecimal(3);

    /// <summary>A whole number in uppercase hexadecimal: <c>255</c> is <c>FF</c>.</summary>
    public sealed class Hex : IDeviceFieldFormatter<int>
    {
        /// <inheritdoc />
        public string Format(int value) => value.ToString("X", CultureInfo.InvariantCulture);

        /// <inheritdoc />
        public int Parse(string text) => int.Parse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
    }

    /// <summary>A moment as whole seconds since 1970-01-01 UTC.</summary>
    public sealed class UnixSeconds : IDeviceFieldFormatter<DateTimeOffset>
    {
        /// <inheritdoc />
        public string Format(DateTimeOffset value) => value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

        /// <inheritdoc />
        public DateTimeOffset Parse(string text)
            => DateTimeOffset.FromUnixTimeSeconds(long.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture));
    }

    /// <summary>A moment as whole milliseconds since 1970-01-01 UTC.</summary>
    public sealed class UnixMilliseconds : IDeviceFieldFormatter<DateTimeOffset>
    {
        /// <inheritdoc />
        public string Format(DateTimeOffset value) => value.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);

        /// <inheritdoc />
        public DateTimeOffset Parse(string text)
            => DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture));
    }

    /// <summary>A decimal stored as a signed 16-bit register scaled by 10^decimals, as many Modbus devices do.</summary>
    public abstract class ScaledInt16(int decimals) : IDeviceBinaryFieldFormatter<decimal>
    {
        private readonly decimal _factor = Factor(decimals);

        /// <inheritdoc />
        public int Size => 2;

        /// <inheritdoc />
        public void Write(decimal value, Span<byte> destination, bool bigEndian)
        {
            var raw = checked((short)decimal.Round(value * _factor));
            if (bigEndian)
            {
                BinaryPrimitives.WriteInt16BigEndian(destination, raw);
            }
            else
            {
                BinaryPrimitives.WriteInt16LittleEndian(destination, raw);
            }
        }

        /// <inheritdoc />
        public decimal Read(ReadOnlySpan<byte> source, bool bigEndian)
            => (bigEndian ? BinaryPrimitives.ReadInt16BigEndian(source) : BinaryPrimitives.ReadInt16LittleEndian(source)) / _factor;
    }

    /// <summary>A decimal stored as a signed 32-bit value scaled by 10^decimals.</summary>
    public abstract class ScaledInt32(int decimals) : IDeviceBinaryFieldFormatter<decimal>
    {
        private readonly decimal _factor = Factor(decimals);

        /// <inheritdoc />
        public int Size => 4;

        /// <inheritdoc />
        public void Write(decimal value, Span<byte> destination, bool bigEndian)
        {
            var raw = checked((int)decimal.Round(value * _factor));
            if (bigEndian)
            {
                BinaryPrimitives.WriteInt32BigEndian(destination, raw);
            }
            else
            {
                BinaryPrimitives.WriteInt32LittleEndian(destination, raw);
            }
        }

        /// <inheritdoc />
        public decimal Read(ReadOnlySpan<byte> source, bool bigEndian)
            => (bigEndian ? BinaryPrimitives.ReadInt32BigEndian(source) : BinaryPrimitives.ReadInt32LittleEndian(source)) / _factor;
    }

    /// <summary>A moment as a 32-bit unsigned count of seconds since 1970-01-01 UTC.</summary>
    public sealed class BinaryUnixSeconds : IDeviceBinaryFieldFormatter<DateTimeOffset>
    {
        /// <inheritdoc />
        public int Size => 4;

        /// <inheritdoc />
        public void Write(DateTimeOffset value, Span<byte> destination, bool bigEndian)
        {
            var raw = checked((uint)value.ToUnixTimeSeconds());
            if (bigEndian)
            {
                BinaryPrimitives.WriteUInt32BigEndian(destination, raw);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(destination, raw);
            }
        }

        /// <inheritdoc />
        public DateTimeOffset Read(ReadOnlySpan<byte> source, bool bigEndian)
            => DateTimeOffset.FromUnixTimeSeconds(bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(source) : BinaryPrimitives.ReadUInt32LittleEndian(source));
    }

    /// <summary>
    /// A whole number in packed BCD, two digits per byte, most significant first: with 3 bytes,
    /// <c>12345</c> is <c>01 23 45</c>. Byte order does not apply.
    /// </summary>
    public abstract class Bcd(int bytes) : IDeviceBinaryFieldFormatter<long>
    {
        /// <inheritdoc />
        public int Size { get; } = bytes > 0 ? bytes : throw new ArgumentOutOfRangeException(nameof(bytes));

        /// <inheritdoc />
        public void Write(long value, Span<byte> destination, bool bigEndian)
        {
            if (value < 0)
            {
                throw new FormatException($"BCD holds no sign, and the value is {value}.");
            }

            for (var index = Size - 1; index >= 0; index--)
            {
                var low = (byte)(value % 10);
                value /= 10;
                var high = (byte)(value % 10);
                value /= 10;
                destination[index] = (byte)((high << 4) | low);
            }

            if (value != 0)
            {
                throw new FormatException($"The value has more digits than {Size} BCD bytes hold.");
            }
        }

        /// <inheritdoc />
        public long Read(ReadOnlySpan<byte> source, bool bigEndian)
        {
            long value = 0;
            foreach (var packed in source)
            {
                var high = packed >> 4;
                var low = packed & 0x0F;
                if (high > 9 || low > 9)
                {
                    throw new FormatException($"0x{packed:X2} is not a BCD byte.");
                }

                value = (value * 100) + (high * 10) + low;
            }

            return value;
        }
    }

    private static decimal Factor(int decimals)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(decimals);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(decimals, 18);
        decimal factor = 1;
        for (var index = 0; index < decimals; index++)
        {
            factor *= 10;
        }

        return factor;
    }
}
