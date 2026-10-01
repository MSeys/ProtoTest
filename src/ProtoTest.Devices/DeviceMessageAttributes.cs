namespace ProtoTest.Devices;

/// <summary>
/// Declares a text message: an optional leading token and the field separator. Without the attribute a
/// message has no prefix and separates its fields with commas. A checksum goes in
/// <see cref="DeviceChecksumAttribute{TChecksum}"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DeviceMessageAttribute(string prefix = "") : Attribute
{
    /// <summary>Gets the leading token, such as <c>$GPGGA</c>, written first and required when parsing.</summary>
    public string Prefix { get; } = prefix ?? string.Empty;

    /// <summary>
    /// Gets or sets the text between fields. Defaults to a comma. An empty separator makes a fixed-width
    /// message, where every field needs a <see cref="DeviceFieldAttribute.Width"/>.
    /// </summary>
    public string Separator { get; set; } = ",";
}

/// <summary>
/// Declares a binary message: fixed-size fields after optional leading bytes, such as a slave address and
/// a function code. Numbers are big-endian unless <see cref="BigEndian"/> is false.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DeviceBinaryMessageAttribute(params byte[] prefix) : Attribute
{
    /// <summary>Gets the bytes every message of this type starts with.</summary>
    public byte[] Prefix { get; } = prefix ?? [];

    /// <summary>Gets or sets whether multi-byte numbers are big-endian. Defaults to true.</summary>
    public bool BigEndian { get; set; } = true;
}

/// <summary>
/// How one field is written and read. On a positional record the field order is the parameter order;
/// on a class with settable properties, <see cref="Order"/> sets it.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, Inherited = false)]
public sealed class DeviceFieldAttribute : Attribute
{
    /// <summary>Creates the attribute; pass an order for a class, leave it out on a positional record.</summary>
    public DeviceFieldAttribute(int order = -1) => Order = order;

    /// <summary>Gets the field's position, counted from zero after the prefix.</summary>
    public int Order { get; }

    /// <summary>
    /// Gets or sets a text field's format: a .NET format string for numbers, dates and times
    /// (<c>0.00</c>, <c>yyyyMMddHHmmss</c>), <c>D</c> for an enum's number, or <c>true|false</c> words for a
    /// boolean. A conversion a format string cannot express belongs in a <see cref="DeviceFormatAttribute{TFormatter}"/>.
    /// </summary>
    public string? Format { get; set; }

    /// <summary>Gets or sets a text field's fixed width; the value is padded to it when written and trimmed when read.</summary>
    public int Width { get; set; }

    /// <summary>Gets or sets the padding character: for <see cref="Width"/> in text, for <see cref="Bytes"/> in a binary string. Defaults to a space in text and a zero byte in binary.</summary>
    public char Pad { get; set; } = '￿';

    /// <summary>Gets or sets whether text padding goes on the left, as for zero-padded numbers. Defaults to the right.</summary>
    public bool PadLeft { get; set; }

    /// <summary>Gets or sets a binary string's or byte array's fixed size in bytes.</summary>
    public int Bytes { get; set; }
}

/// <summary>The checksum a message carries, as <see cref="DeviceChecksumAttribute{TChecksum}"/> declares it.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public abstract class DeviceChecksumAttribute : Attribute
{
    private protected DeviceChecksumAttribute()
    {
    }

    internal abstract object Create();
}

/// <summary>
/// Declares the message's checksum: an <see cref="IDeviceMessageChecksum"/> for a text message, such as
/// <see cref="NmeaChecksum"/>, or an <see cref="IDeviceBinaryChecksum"/> for a binary one, such as
/// <see cref="Crc16Modbus"/>.
/// </summary>
public sealed class DeviceChecksumAttribute<TChecksum> : DeviceChecksumAttribute
    where TChecksum : new()
{
    internal override object Create() => new TChecksum();
}

/// <summary>The formatter a field uses, as <see cref="DeviceFormatAttribute{TFormatter}"/> declares it.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, Inherited = false)]
public abstract class DeviceFormatAttribute : Attribute
{
    private protected DeviceFormatAttribute()
    {
    }

    internal abstract object Create();
}

/// <summary>
/// Declares how a field converts, for values a format string cannot express: a scaled number, hex, Unix
/// time, or a device's own encoding. <typeparamref name="TFormatter"/> implements
/// <see cref="IDeviceFieldFormatter{T}"/> for a text message or <see cref="IDeviceBinaryFieldFormatter{T}"/>
/// for a binary one, where <c>T</c> is the field's type.
/// </summary>
public sealed class DeviceFormatAttribute<TFormatter> : DeviceFormatAttribute
    where TFormatter : new()
{
    internal override object Create() => new TFormatter();
}
