namespace ProtoTest.Devices;

using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;

/// <summary>A data string or byte block that does not match its message type: the prefix, a field, the length or the checksum.</summary>
public sealed class DeviceMessageFormatException : FormatException
{
    public DeviceMessageFormatException(string message)
        : base(message)
    {
    }

    public DeviceMessageFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Writes a message type to what the device sends and reads it back, so a test builds
/// <c>new MeterReading("M-1", 12.5m)</c> instead of concatenating <c>"$MTR,M-1,12.50*4B"</c>. A type with
/// <see cref="DeviceBinaryMessageAttribute"/> is a fixed-layout byte block; any other type is a line of text.
/// Text values use the invariant culture.
/// </summary>
public static class DeviceMessage
{
    private static readonly ConcurrentDictionary<Type, MessageLayout> Layouts = new();

    /// <summary>Writes a text message as its data string, checksum included.</summary>
    public static string Format<TMessage>(TMessage message)
        where TMessage : notnull
    {
        ArgumentNullException.ThrowIfNull(message);
        return LayoutOf(message.GetType()).AsText().Format(message);
    }

    /// <summary>Reads a data string as <typeparamref name="TMessage"/>, or fails naming what did not match.</summary>
    /// <exception cref="DeviceMessageFormatException">The prefix, a field, the field count or the checksum does not match.</exception>
    public static TMessage Parse<TMessage>(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return (TMessage)LayoutOf(typeof(TMessage)).AsText().Parse(text);
    }

    /// <summary>Reads a data string as <typeparamref name="TMessage"/>, or returns false when it does not match.</summary>
    public static bool TryParse<TMessage>(string? text, out TMessage message)
    {
        message = default!;
        if (text is null)
        {
            return false;
        }

        try
        {
            message = Parse<TMessage>(text);
            return true;
        }
        catch (DeviceMessageFormatException)
        {
            return false;
        }
    }

    /// <summary>Writes a binary message as its bytes, prefix and checksum included.</summary>
    public static byte[] ToBytes<TMessage>(TMessage message)
        where TMessage : notnull
    {
        ArgumentNullException.ThrowIfNull(message);
        return LayoutOf(message.GetType()).AsBinary().Write(message);
    }

    /// <summary>Reads bytes as a binary <typeparamref name="TMessage"/>, or fails naming what did not match.</summary>
    /// <exception cref="DeviceMessageFormatException">The prefix, the length, a field or the checksum does not match.</exception>
    public static TMessage FromBytes<TMessage>(ReadOnlySpan<byte> bytes)
        => (TMessage)LayoutOf(typeof(TMessage)).AsBinary().Read(bytes);

    /// <summary>Writes a message as the frame a device sends: text for a text message, bytes for a binary one.</summary>
    public static DeviceFrame ToFrame<TMessage>(TMessage message)
        where TMessage : notnull
    {
        ArgumentNullException.ThrowIfNull(message);
        var layout = LayoutOf(message.GetType());
        return layout is BinaryLayout binary
            ? DeviceFrame.Binary(binary.Write(message))
            : DeviceFrame.Text(layout.AsText().Format(message));
    }

    /// <summary>Reads a frame as <typeparamref name="TMessage"/>, or fails naming what did not match.</summary>
    public static TMessage Read<TMessage>(DeviceFrame frame)
    {
        var layout = LayoutOf(typeof(TMessage));
        if (layout is BinaryLayout binary)
        {
            return (TMessage)binary.Read(frame.Payload.Span);
        }

        if (!frame.TryGetText(out var text))
        {
            throw new DeviceMessageFormatException($"The frame is {frame}, and {typeof(TMessage).Name} is a text message.");
        }

        return (TMessage)layout.AsText().Parse(text);
    }

    /// <summary>Reads a frame as <typeparamref name="TMessage"/>, or returns false when it is another message.</summary>
    public static bool TryRead<TMessage>(DeviceFrame frame, out TMessage message)
    {
        message = default!;
        try
        {
            message = Read<TMessage>(frame);
            return true;
        }
        catch (DeviceMessageFormatException)
        {
            return false;
        }
    }

    internal static bool Matches(Type messageType, DeviceFrame frame)
    {
        var layout = LayoutOf(messageType);
        try
        {
            if (layout is BinaryLayout binary)
            {
                binary.Read(frame.Payload.Span);
            }
            else if (frame.TryGetText(out var text))
            {
                layout.AsText().Parse(text);
            }
            else
            {
                return false;
            }

            return true;
        }
        catch (DeviceMessageFormatException)
        {
            return false;
        }
    }

    internal static string DescribePrefix(Type messageType) => LayoutOf(messageType).Prefix;

    private static MessageLayout LayoutOf(Type type) => Layouts.GetOrAdd(type, MessageLayout.Create);

    private abstract class MessageLayout(Type type, MessageField[] fields, ConstructorInfo? constructor)
    {
        protected Type Type { get; } = type;

        protected MessageField[] Fields { get; } = fields;

        public abstract string Prefix { get; }

        public static MessageLayout Create(Type type)
        {
            var (fields, constructor) = Discover(type);
            var checksum = type.GetCustomAttribute<DeviceChecksumAttribute>()?.Create();
            if (type.GetCustomAttribute<DeviceBinaryMessageAttribute>() is { } binary)
            {
                if (type.GetCustomAttribute<DeviceMessageAttribute>() is not null)
                {
                    throw new InvalidOperationException($"{type.Name} declares both [DeviceMessage] and [DeviceBinaryMessage]; a message is text or binary.");
                }

                return new BinaryLayout(type, binary, fields, constructor, checksum switch
                {
                    null => null,
                    IDeviceBinaryChecksum binaryChecksum => binaryChecksum,
                    _ => throw new InvalidOperationException($"{type.Name} is binary, and its checksum {checksum.GetType().Name} is a text checksum; use an IDeviceBinaryChecksum.")
                });
            }

            return new TextLayout(type, type.GetCustomAttribute<DeviceMessageAttribute>() ?? new DeviceMessageAttribute(), fields, constructor, checksum switch
            {
                null => null,
                IDeviceMessageChecksum textChecksum => textChecksum,
                _ => throw new InvalidOperationException($"{type.Name} is text, and its checksum {checksum.GetType().Name} is a binary checksum; use an IDeviceMessageChecksum.")
            });
        }

        public TextLayout AsText() => this as TextLayout
            ?? throw new InvalidOperationException($"{Type.Name} is a binary message; use ToBytes, FromBytes or the frame methods.");

        public BinaryLayout AsBinary() => this as BinaryLayout
            ?? throw new InvalidOperationException($"{Type.Name} is a text message; use Format, Parse or the frame methods.");

        protected object Construct(object?[] values)
        {
            if (constructor is not null)
            {
                return constructor.Invoke(values);
            }

            var message = Activator.CreateInstance(Type)!;
            for (var index = 0; index < Fields.Length; index++)
            {
                Fields[index].Property.SetValue(message, values[index]);
            }

            return message;
        }

        protected DeviceMessageFormatException Mismatch(string shown, string reason)
            => new($"{shown} is not a {Type.Name}: {reason}.");

        private static (MessageField[] Fields, ConstructorInfo? Constructor) Discover(Type type)
        {
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
                .ToArray();

            // A positional record: the widest public constructor whose parameters all name a property.
            var constructor = type.GetConstructors()
                .Where(candidate => candidate.GetParameters().Length > 0
                    && candidate.GetParameters().All(parameter => Find(properties, parameter.Name) is not null))
                .MaxBy(candidate => candidate.GetParameters().Length);
            if (constructor is not null)
            {
                return (constructor.GetParameters()
                    .Select(parameter =>
                    {
                        var property = Find(properties, parameter.Name)!;
                        return new MessageField(
                            type,
                            property,
                            parameter.GetCustomAttribute<DeviceFieldAttribute>() ?? property.GetCustomAttribute<DeviceFieldAttribute>() ?? new DeviceFieldAttribute(),
                            parameter.GetCustomAttribute<DeviceFormatAttribute>() ?? property.GetCustomAttribute<DeviceFormatAttribute>());
                    })
                    .ToArray(), constructor);
            }

            var fields = properties
                .Where(property => property.CanWrite)
                .Select(property => (Property: property, Field: property.GetCustomAttribute<DeviceFieldAttribute>()))
                .Where(entry => entry.Field is not null)
                .Select(entry => entry.Field!.Order < 0
                    ? throw new InvalidOperationException(
                        $"{type.Name}.{entry.Property.Name} needs a field order: [DeviceField(0)], [DeviceField(1)], and so on.")
                    : new MessageField(type, entry.Property, entry.Field, entry.Property.GetCustomAttribute<DeviceFormatAttribute>()))
                .OrderBy(field => field.Attribute.Order)
                .ToArray();
            if (fields.Length == 0)
            {
                throw new InvalidOperationException(
                    $"{type.Name} has no message fields. Use a positional record, or mark settable properties with [DeviceField(order)].");
            }

            if (type.GetConstructor(Type.EmptyTypes) is null)
            {
                throw new InvalidOperationException($"{type.Name} needs a public parameterless constructor to be read.");
            }

            return (fields, null);
        }

        private static PropertyInfo? Find(PropertyInfo[] properties, string? name)
            => properties.FirstOrDefault(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class TextLayout : MessageLayout
    {
        private readonly string _prefix;
        private readonly string _separator;
        private readonly IDeviceMessageChecksum? _checksum;

        public TextLayout(Type type, DeviceMessageAttribute attribute, MessageField[] fields, ConstructorInfo? constructor, IDeviceMessageChecksum? checksum)
            : base(type, fields, constructor)
        {
            _prefix = attribute.Prefix;
            _separator = attribute.Separator ?? string.Empty;
            _checksum = checksum;
            foreach (var field in fields)
            {
                field.PrepareText(fixedWidth: _separator.Length == 0);
            }
        }

        public override string Prefix => _prefix;

        public string Format(object message)
        {
            var values = Fields.Select(field => field.WriteText(field.Property.GetValue(message), _separator));
            var parts = _prefix.Length > 0 ? values.Prepend(_prefix) : values;
            var text = string.Join(_separator, parts);
            return _checksum is null ? text : text + _checksum.Separator + _checksum.Compute(text);
        }

        public object Parse(string text)
        {
            var shown = $"'{text}'";
            var body = text;
            if (_checksum is not null)
            {
                var at = text.LastIndexOf(_checksum.Separator, StringComparison.Ordinal);
                if (at < 0)
                {
                    throw Mismatch(shown, $"it has no '{_checksum.Separator}' before a checksum");
                }

                body = text[..at];
                var given = text[(at + _checksum.Separator.Length)..];
                var expected = _checksum.Compute(body);
                if (!string.Equals(given, expected, StringComparison.OrdinalIgnoreCase))
                {
                    throw Mismatch(shown, $"its checksum is {given}, but the body computes to {expected}");
                }
            }

            if (!body.StartsWith(_prefix, StringComparison.Ordinal))
            {
                throw Mismatch(shown, $"it does not start with '{_prefix}'");
            }

            var rest = body[_prefix.Length..];
            string[] raw;
            if (_separator.Length > 0)
            {
                if (_prefix.Length > 0)
                {
                    if (!rest.StartsWith(_separator, StringComparison.Ordinal))
                    {
                        throw Mismatch(shown, $"'{_prefix}' is not followed by '{_separator}'");
                    }

                    rest = rest[_separator.Length..];
                }

                raw = rest.Split(_separator);
                if (raw.Length != Fields.Length)
                {
                    throw Mismatch(shown, $"it has {raw.Length} fields, and {Type.Name} has {Fields.Length}");
                }
            }
            else
            {
                var total = Fields.Sum(field => field.Attribute.Width);
                if (rest.Length != total)
                {
                    throw Mismatch(shown, $"it is {rest.Length} characters after the prefix, and {Type.Name}'s fields are {total}");
                }

                raw = new string[Fields.Length];
                var offset = 0;
                for (var index = 0; index < Fields.Length; index++)
                {
                    raw[index] = rest.Substring(offset, Fields[index].Attribute.Width);
                    offset += Fields[index].Attribute.Width;
                }
            }

            var values = new object?[Fields.Length];
            for (var index = 0; index < Fields.Length; index++)
            {
                values[index] = Fields[index].ReadText(raw[index], index, shown);
            }

            return Construct(values);
        }
    }

    private sealed class BinaryLayout : MessageLayout
    {
        private readonly byte[] _prefix;
        private readonly bool _bigEndian;
        private readonly IDeviceBinaryChecksum? _checksum;
        private readonly int _length;

        public BinaryLayout(Type type, DeviceBinaryMessageAttribute attribute, MessageField[] fields, ConstructorInfo? constructor, IDeviceBinaryChecksum? checksum)
            : base(type, fields, constructor)
        {
            _prefix = attribute.Prefix;
            _bigEndian = attribute.BigEndian;
            _checksum = checksum;
            foreach (var field in fields)
            {
                field.PrepareBinary();
            }

            _length = _prefix.Length + fields.Sum(field => field.ByteSize) + (checksum?.Size ?? 0);
        }

        public override string Prefix => Convert.ToHexString(_prefix);

        public byte[] Write(object message)
        {
            var bytes = new byte[_length];
            _prefix.CopyTo(bytes, 0);
            var offset = _prefix.Length;
            foreach (var field in Fields)
            {
                field.WriteBinary(field.Property.GetValue(message), bytes.AsSpan(offset, field.ByteSize), _bigEndian);
                offset += field.ByteSize;
            }

            _checksum?.Compute(bytes.AsSpan(0, offset), bytes.AsSpan(offset));
            return bytes;
        }

        public object Read(ReadOnlySpan<byte> bytes)
        {
            var shown = bytes.Length <= 32 ? Convert.ToHexString(bytes) : $"{Convert.ToHexString(bytes[..32])}...";
            if (bytes.Length != _length)
            {
                throw Mismatch(shown, $"it is {bytes.Length} bytes, and {Type.Name} is {_length}");
            }

            if (!bytes.StartsWith(_prefix))
            {
                throw Mismatch(shown, $"it does not start with {Convert.ToHexString(_prefix)}");
            }

            var bodyLength = _length - (_checksum?.Size ?? 0);
            if (_checksum is not null)
            {
                Span<byte> expected = stackalloc byte[_checksum.Size];
                _checksum.Compute(bytes[..bodyLength], expected);
                if (!bytes[bodyLength..].SequenceEqual(expected))
                {
                    throw Mismatch(shown, $"its checksum is {Convert.ToHexString(bytes[bodyLength..])}, but the body computes to {Convert.ToHexString(expected)}");
                }
            }

            var values = new object?[Fields.Length];
            var offset = _prefix.Length;
            for (var index = 0; index < Fields.Length; index++)
            {
                values[index] = Fields[index].ReadBinary(bytes.Slice(offset, Fields[index].ByteSize), _bigEndian, index, shown);
                offset += Fields[index].ByteSize;
            }

            return Construct(values);
        }
    }

    private sealed class MessageField(Type messageType, PropertyInfo property, DeviceFieldAttribute attribute, DeviceFormatAttribute? format)
    {
        private object? _formatter;
        private MethodInfo? _formatterWrite;
        private MethodInfo? _formatterRead;

        public PropertyInfo Property { get; } = property;

        public DeviceFieldAttribute Attribute { get; } = attribute;

        public int ByteSize { get; private set; }

        private string Name => $"{messageType.Name}.{Property.Name}";

        private Type ValueType => Nullable.GetUnderlyingType(Property.PropertyType) ?? Property.PropertyType;

        private char TextPad => Attribute.Pad == '￿' ? ' ' : Attribute.Pad;

        private byte BinaryPad => Attribute.Pad == '￿' ? (byte)0 : (byte)Attribute.Pad;

        public void PrepareText(bool fixedWidth)
        {
            if (fixedWidth && Attribute.Width <= 0)
            {
                throw new InvalidOperationException($"{messageType.Name} has no separator, so every field needs a width; {Property.Name} has none.");
            }

            if (format is not null)
            {
                BindFormatter(typeof(IDeviceFieldFormatter<>), "a text", "IDeviceFieldFormatter");
                return;
            }

            if (!TextTypes.Supports(ValueType))
            {
                throw new InvalidOperationException(
                    $"{Name} is a {Property.PropertyType.Name}, which a text field does not support; use a string, number, boolean, enum, " +
                    "date or time, or declare a [DeviceFormat<TFormatter>].");
            }
        }

        public void PrepareBinary()
        {
            if (format is not null)
            {
                BindFormatter(typeof(IDeviceBinaryFieldFormatter<>), "a binary", "IDeviceBinaryFieldFormatter");
                ByteSize = (int)_formatter!.GetType().GetProperty(nameof(IDeviceBinaryFieldFormatter<int>.Size))!.GetValue(_formatter)!;
                return;
            }

            if (Nullable.GetUnderlyingType(Property.PropertyType) is not null)
            {
                throw new InvalidOperationException($"{Name} is nullable, and a binary field always holds bytes; use a non-nullable type.");
            }

            ByteSize = BinaryTypes.SizeOf(ValueType, Attribute.Bytes)
                ?? throw new InvalidOperationException(
                    $"{Name} is a {Property.PropertyType.Name}, which a binary field does not support without a size; " +
                    "use a number, boolean or enum, a string or byte[] with [DeviceField(Bytes = n)], or declare a [DeviceFormat<TFormatter>].");
        }

        public string WriteText(object? value, string separator)
        {
            var text = value is null ? string.Empty
                : _formatter is not null ? (string)_formatterWrite!.Invoke(_formatter, [value])!
                : TextTypes.Format(value, Attribute.Format);
            if (separator.Length > 0 && text.Contains(separator, StringComparison.Ordinal))
            {
                throw new DeviceMessageFormatException($"{Name} is '{text}', which contains the separator '{separator}', so the message could not be read back.");
            }

            if (Attribute.Width > 0)
            {
                if (text.Length > Attribute.Width)
                {
                    throw new DeviceMessageFormatException($"{Name} is '{text}', longer than its width of {Attribute.Width}.");
                }

                text = Attribute.PadLeft ? text.PadLeft(Attribute.Width, TextPad) : text.PadRight(Attribute.Width, TextPad);
            }

            return text;
        }

        public object? ReadText(string raw, int index, string shown)
        {
            var value = Attribute.Width > 0 ? (Attribute.PadLeft ? raw.TrimStart(TextPad) : raw.TrimEnd(TextPad)) : raw;
            if (value.Length == 0)
            {
                if (ValueType == typeof(string) && _formatter is null)
                {
                    return Property.PropertyType == typeof(string) ? string.Empty : null;
                }

                if (!Property.PropertyType.IsValueType || Nullable.GetUnderlyingType(Property.PropertyType) is not null)
                {
                    return null;
                }

                if (Attribute.Width > 0 && Attribute.PadLeft && TextPad == '0')
                {
                    value = "0";
                }
            }

            try
            {
                return _formatter is not null
                    ? Unwrap(() => _formatterRead!.Invoke(_formatter, [value]))
                    : TextTypes.Parse(value, ValueType, Attribute.Format);
            }
            catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
            {
                throw new DeviceMessageFormatException(
                    $"{shown} is not a {messageType.Name}: field {index} ({Property.Name}) is '{raw}', which is not a {ValueType.Name}" +
                    (_formatter is not null ? $" for {_formatter.GetType().Name}." : Attribute.Format is null ? "." : $" in the format '{Attribute.Format}'."),
                    exception);
            }
        }

        public void WriteBinary(object? value, Span<byte> destination, bool bigEndian)
        {
            try
            {
                if (_formatter is not null)
                {
                    // A span cannot cross reflection, so the formatter writes into an array copied back.
                    var buffer = new byte[ByteSize];
                    BinaryBridge.Write(_formatter, ValueType, value, buffer, bigEndian);
                    buffer.CopyTo(destination);
                    return;
                }

                BinaryTypes.Write(value, ValueType, destination, bigEndian, BinaryPad);
            }
            catch (Exception exception) when (exception is ArgumentException or OverflowException or FormatException)
            {
                throw new DeviceMessageFormatException($"{Name} is '{value}', which does not fit its {ByteSize} bytes: {exception.Message}", exception);
            }
        }

        public object? ReadBinary(ReadOnlySpan<byte> source, bool bigEndian, int index, string shown)
        {
            try
            {
                return _formatter is not null
                    ? BinaryBridge.Read(_formatter, ValueType, source.ToArray(), bigEndian)
                    : BinaryTypes.Read(source, ValueType, bigEndian, BinaryPad);
            }
            catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
            {
                throw new DeviceMessageFormatException(
                    $"{shown} is not a {messageType.Name}: field {index} ({Property.Name}) is {Convert.ToHexString(source)}, which is not a {ValueType.Name}.",
                    exception);
            }
        }

        private void BindFormatter(Type contract, string kind, string contractName)
        {
            var formatter = format!.Create();
            var implemented = formatter.GetType().GetInterfaces()
                .FirstOrDefault(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == contract);
            if (implemented is null)
            {
                throw new InvalidOperationException(
                    $"{Name}'s formatter {formatter.GetType().Name} is not {kind} formatter; implement {contractName}<{ValueType.Name}>.");
            }

            var handles = implemented.GetGenericArguments()[0];
            if (handles != ValueType)
            {
                throw new InvalidOperationException(
                    $"{Name} is a {ValueType.Name}, and its formatter {formatter.GetType().Name} converts a {handles.Name}.");
            }

            _formatter = formatter;
            _formatterWrite = implemented.GetMethod(contract == typeof(IDeviceFieldFormatter<>) ? "Format" : "Write");
            _formatterRead = implemented.GetMethod(contract == typeof(IDeviceFieldFormatter<>) ? "Parse" : "Read");
        }

        private static object? Unwrap(Func<object?> call)
        {
            try
            {
                return call();
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                throw exception.InnerException;
            }
        }
    }

    /// <summary>Calls a binary formatter through a closed generic helper, since spans cannot cross reflection.</summary>
    private static class BinaryBridge
    {
        private static readonly MethodInfo WriteMethod = typeof(BinaryBridge).GetMethod(nameof(WriteCore), BindingFlags.NonPublic | BindingFlags.Static)!;
        private static readonly MethodInfo ReadMethod = typeof(BinaryBridge).GetMethod(nameof(ReadCore), BindingFlags.NonPublic | BindingFlags.Static)!;

        public static void Write(object formatter, Type valueType, object? value, byte[] buffer, bool bigEndian)
            => Invoke(WriteMethod.MakeGenericMethod(valueType), [formatter, value, buffer, bigEndian]);

        public static object? Read(object formatter, Type valueType, byte[] source, bool bigEndian)
            => Invoke(ReadMethod.MakeGenericMethod(valueType), [formatter, source, bigEndian]);

        private static void WriteCore<T>(object formatter, T value, byte[] buffer, bool bigEndian)
            => ((IDeviceBinaryFieldFormatter<T>)formatter).Write(value, buffer, bigEndian);

        private static T ReadCore<T>(object formatter, byte[] source, bool bigEndian)
            => ((IDeviceBinaryFieldFormatter<T>)formatter).Read(source, bigEndian);

        private static object? Invoke(MethodInfo method, object?[] arguments)
        {
            try
            {
                return method.Invoke(null, arguments);
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                throw exception.InnerException;
            }
        }
    }

    private static class TextTypes
    {
        public static bool Supports(Type type)
            => type == typeof(string) || type == typeof(bool) || type == typeof(char) || type == typeof(Guid) || type.IsEnum
                || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan)
                || type == typeof(DateOnly) || type == typeof(TimeOnly)
                || NumberParse(type) is not null;

        public static string Format(object value, string? format) => value switch
        {
            string text => text,
            bool flag => Booleans(format).Split('|')[flag ? 0 : 1],
            Enum enumValue => format is null ? enumValue.ToString() : enumValue.ToString(format),
            IFormattable formattable => formattable.ToString(format, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

        public static object Parse(string value, Type type, string? format)
        {
            var culture = CultureInfo.InvariantCulture;
            if (type == typeof(string))
            {
                return value;
            }

            if (type == typeof(bool))
            {
                var words = Booleans(format).Split('|');
                return string.Equals(value, words[0], StringComparison.OrdinalIgnoreCase) ? true
                    : string.Equals(value, words[1], StringComparison.OrdinalIgnoreCase) ? false
                    : throw new FormatException($"Expected '{words[0]}' or '{words[1]}'.");
            }

            if (type.IsEnum)
            {
                return Enum.Parse(type, value, ignoreCase: true);
            }

            if (type == typeof(DateTimeOffset))
            {
                return format is null
                    ? DateTimeOffset.Parse(value, culture, DateTimeStyles.AssumeUniversal)
                    : DateTimeOffset.ParseExact(value, format, culture, DateTimeStyles.AssumeUniversal);
            }

            if (type == typeof(DateTime))
            {
                const DateTimeStyles utc = DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal;
                return format is null ? DateTime.Parse(value, culture, utc) : DateTime.ParseExact(value, format, culture, utc);
            }

            if (type == typeof(TimeSpan))
            {
                return format is null ? TimeSpan.Parse(value, culture) : TimeSpan.ParseExact(value, format, culture);
            }

            if (type == typeof(DateOnly))
            {
                return format is null ? DateOnly.Parse(value, culture) : DateOnly.ParseExact(value, format, culture);
            }

            if (type == typeof(TimeOnly))
            {
                return format is null ? TimeOnly.Parse(value, culture) : TimeOnly.ParseExact(value, format, culture);
            }

            if (type == typeof(char))
            {
                return value.Length == 1 ? value[0] : throw new FormatException("Expected one character.");
            }

            if (type == typeof(Guid))
            {
                return Guid.Parse(value);
            }

            var parse = NumberParse(type) ?? throw new FormatException($"A {type.Name} field cannot be read.");
            var styles = type == typeof(decimal) || type == typeof(double) || type == typeof(float) || type == typeof(Half)
                ? NumberStyles.Float
                : NumberStyles.Integer;
            try
            {
                return parse.Invoke(null, [value, styles, culture])!;
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                throw exception.InnerException;
            }
        }

        private static MethodInfo? NumberParse(Type type)
            => type.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, [typeof(string), typeof(NumberStyles), typeof(IFormatProvider)]);

        private static string Booleans(string? format)
            => format is not null && format.Count(character => character == '|') == 1 ? format : "1|0";
    }

    private static class BinaryTypes
    {
        public static int? SizeOf(Type type, int bytes)
        {
            if (type.IsEnum)
            {
                type = Enum.GetUnderlyingType(type);
            }

            return type == typeof(byte) || type == typeof(sbyte) || type == typeof(bool) ? 1
                : type == typeof(short) || type == typeof(ushort) ? 2
                : type == typeof(int) || type == typeof(uint) || type == typeof(float) ? 4
                : type == typeof(long) || type == typeof(ulong) || type == typeof(double) ? 8
                : (type == typeof(string) || type == typeof(byte[])) && bytes > 0 ? bytes
                : null;
        }

        public static void Write(object? value, Type type, Span<byte> destination, bool bigEndian, byte pad)
        {
            if (type.IsEnum)
            {
                value = Convert.ChangeType(value, Enum.GetUnderlyingType(type), CultureInfo.InvariantCulture);
                type = Enum.GetUnderlyingType(type);
            }

            switch (value)
            {
                case byte number:
                    destination[0] = number;
                    break;
                case sbyte number:
                    destination[0] = unchecked((byte)number);
                    break;
                case bool flag:
                    destination[0] = flag ? (byte)1 : (byte)0;
                    break;
                case short number:
                    if (bigEndian) BinaryPrimitives.WriteInt16BigEndian(destination, number); else BinaryPrimitives.WriteInt16LittleEndian(destination, number);
                    break;
                case ushort number:
                    if (bigEndian) BinaryPrimitives.WriteUInt16BigEndian(destination, number); else BinaryPrimitives.WriteUInt16LittleEndian(destination, number);
                    break;
                case int number:
                    if (bigEndian) BinaryPrimitives.WriteInt32BigEndian(destination, number); else BinaryPrimitives.WriteInt32LittleEndian(destination, number);
                    break;
                case uint number:
                    if (bigEndian) BinaryPrimitives.WriteUInt32BigEndian(destination, number); else BinaryPrimitives.WriteUInt32LittleEndian(destination, number);
                    break;
                case long number:
                    if (bigEndian) BinaryPrimitives.WriteInt64BigEndian(destination, number); else BinaryPrimitives.WriteInt64LittleEndian(destination, number);
                    break;
                case ulong number:
                    if (bigEndian) BinaryPrimitives.WriteUInt64BigEndian(destination, number); else BinaryPrimitives.WriteUInt64LittleEndian(destination, number);
                    break;
                case float number:
                    if (bigEndian) BinaryPrimitives.WriteSingleBigEndian(destination, number); else BinaryPrimitives.WriteSingleLittleEndian(destination, number);
                    break;
                case double number:
                    if (bigEndian) BinaryPrimitives.WriteDoubleBigEndian(destination, number); else BinaryPrimitives.WriteDoubleLittleEndian(destination, number);
                    break;
                case string text:
                    var encoded = Encoding.ASCII.GetBytes(text);
                    if (encoded.Length > destination.Length)
                    {
                        throw new ArgumentException($"'{text}' is {encoded.Length} bytes.");
                    }

                    destination.Fill(pad);
                    encoded.CopyTo(destination);
                    break;
                case byte[] block:
                    if (block.Length != destination.Length)
                    {
                        throw new ArgumentException($"The block is {block.Length} bytes.");
                    }

                    block.CopyTo(destination);
                    break;
                case null when type == typeof(string):
                    destination.Fill(pad);
                    break;
                default:
                    throw new ArgumentException($"A {type.Name} value cannot be written.");
            }
        }

        public static object Read(ReadOnlySpan<byte> source, Type type, bool bigEndian, byte pad)
        {
            var target = type.IsEnum ? Enum.GetUnderlyingType(type) : type;
            object value = target == typeof(byte) ? source[0]
                : target == typeof(sbyte) ? unchecked((sbyte)source[0])
                : target == typeof(bool) ? source[0] != 0
                : target == typeof(short) ? (bigEndian ? BinaryPrimitives.ReadInt16BigEndian(source) : BinaryPrimitives.ReadInt16LittleEndian(source))
                : target == typeof(ushort) ? (bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(source) : BinaryPrimitives.ReadUInt16LittleEndian(source))
                : target == typeof(int) ? (bigEndian ? BinaryPrimitives.ReadInt32BigEndian(source) : BinaryPrimitives.ReadInt32LittleEndian(source))
                : target == typeof(uint) ? (bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(source) : BinaryPrimitives.ReadUInt32LittleEndian(source))
                : target == typeof(long) ? (bigEndian ? BinaryPrimitives.ReadInt64BigEndian(source) : BinaryPrimitives.ReadInt64LittleEndian(source))
                : target == typeof(ulong) ? (bigEndian ? BinaryPrimitives.ReadUInt64BigEndian(source) : BinaryPrimitives.ReadUInt64LittleEndian(source))
                : target == typeof(float) ? (bigEndian ? BinaryPrimitives.ReadSingleBigEndian(source) : BinaryPrimitives.ReadSingleLittleEndian(source))
                : target == typeof(double) ? (bigEndian ? BinaryPrimitives.ReadDoubleBigEndian(source) : BinaryPrimitives.ReadDoubleLittleEndian(source))
                : target == typeof(string) ? Encoding.ASCII.GetString(source.TrimEnd(pad))
                : target == typeof(byte[]) ? source.ToArray()
                : throw new FormatException($"A {type.Name} field cannot be read.");
            if (type.IsEnum)
            {
                var result = Enum.ToObject(type, value);
                return Enum.IsDefined(type, result) ? result : throw new FormatException($"{value} is not a {type.Name}.");
            }

            return value;
        }
    }
}
