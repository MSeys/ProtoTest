namespace ProtoTest.Devices;

using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;

/// <summary>
/// Declares how a class or record maps to a device data string: an optional leading token, the field
/// separator, and an optional checksum. Without the attribute a message has no prefix, separates its
/// fields with commas and carries no checksum.
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

    /// <summary>Gets or sets a checksum type implementing <see cref="IDeviceMessageChecksum"/>, such as <see cref="NmeaChecksum"/>.</summary>
    public Type? Checksum { get; set; }
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
    /// Gets or sets the format: a .NET format string for numbers, dates and times (<c>0.00</c>,
    /// <c>yyyyMMddHHmmss</c>), <c>D</c> for an enum's number, or <c>true|false</c> text for a boolean.
    /// </summary>
    public string? Format { get; set; }

    /// <summary>Gets or sets a fixed width; the value is padded to it when written and trimmed when read.</summary>
    public int Width { get; set; }

    /// <summary>Gets or sets the padding character for <see cref="Width"/>. Defaults to a space.</summary>
    public char Pad { get; set; } = ' ';

    /// <summary>Gets or sets whether padding goes on the left, as for zero-padded numbers. Defaults to the right.</summary>
    public bool PadLeft { get; set; }
}

/// <summary>A checksum a device message carries after its body, such as NMEA's <c>*hh</c>.</summary>
public interface IDeviceMessageChecksum
{
    /// <summary>Gets the text between the body and the checksum, such as <c>*</c>.</summary>
    string Separator { get; }

    /// <summary>Computes the checksum text for the message body (everything before the separator).</summary>
    string Compute(string body);
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

/// <summary>A data string that does not match its message type: the prefix, a field, the field count or the checksum.</summary>
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
/// Writes a message type to its device data string and reads it back, so a test builds
/// <c>new MeterReading("M-1", 12.5m)</c> instead of concatenating <c>"$MTR,M-1,12.50*4B"</c>. Values are
/// written and read with the invariant culture.
/// </summary>
public static class DeviceMessage
{
    private static readonly ConcurrentDictionary<Type, MessageLayout> Layouts = new();

    /// <summary>Writes <paramref name="message"/> as its data string, checksum included.</summary>
    public static string Format<TMessage>(TMessage message)
        where TMessage : notnull
    {
        ArgumentNullException.ThrowIfNull(message);
        return LayoutOf(message.GetType()).Format(message);
    }

    /// <summary>Writes <paramref name="message"/> as a text frame.</summary>
    public static DeviceFrame ToFrame<TMessage>(TMessage message)
        where TMessage : notnull
        => DeviceFrame.Text(Format(message));

    /// <summary>Reads a data string as <typeparamref name="TMessage"/>, or fails naming what did not match.</summary>
    /// <exception cref="DeviceMessageFormatException">The prefix, a field, the field count or the checksum does not match.</exception>
    public static TMessage Parse<TMessage>(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return (TMessage)LayoutOf(typeof(TMessage)).Parse(text);
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

    private static MessageLayout LayoutOf(Type type) => Layouts.GetOrAdd(type, MessageLayout.Create);

    private sealed class MessageLayout
    {
        private readonly Type _type;
        private readonly string _prefix;
        private readonly string _separator;
        private readonly IDeviceMessageChecksum? _checksum;
        private readonly MessageField[] _fields;
        private readonly ConstructorInfo? _constructor;

        private MessageLayout(
            Type type,
            DeviceMessageAttribute attribute,
            MessageField[] fields,
            ConstructorInfo? constructor)
        {
            _type = type;
            _prefix = attribute.Prefix;
            _separator = attribute.Separator ?? string.Empty;
            _fields = fields;
            _constructor = constructor;
            if (attribute.Checksum is { } checksumType)
            {
                _checksum = Activator.CreateInstance(checksumType) as IDeviceMessageChecksum
                    ?? throw new InvalidOperationException(
                        $"{type.Name}'s checksum type {checksumType.Name} does not implement IDeviceMessageChecksum.");
            }
        }

        public static MessageLayout Create(Type type)
        {
            var attribute = type.GetCustomAttribute<DeviceMessageAttribute>() ?? new DeviceMessageAttribute();
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
                .ToArray();

            // A positional record: the widest public constructor whose parameters all name a property.
            var constructor = type.GetConstructors()
                .Where(candidate => candidate.GetParameters().Length > 0
                    && candidate.GetParameters().All(parameter => Find(properties, parameter.Name) is not null))
                .MaxBy(candidate => candidate.GetParameters().Length);
            MessageField[] fields;
            if (constructor is not null)
            {
                fields = constructor.GetParameters()
                    .Select(parameter =>
                    {
                        var property = Find(properties, parameter.Name)!;
                        var field = parameter.GetCustomAttribute<DeviceFieldAttribute>()
                            ?? property.GetCustomAttribute<DeviceFieldAttribute>()
                            ?? new DeviceFieldAttribute();
                        return new MessageField(property, field);
                    })
                    .ToArray();
            }
            else
            {
                fields = properties
                    .Where(property => property.CanWrite)
                    .Select(property => (Property: property, Field: property.GetCustomAttribute<DeviceFieldAttribute>()))
                    .Where(entry => entry.Field is not null)
                    .Select(entry =>
                    {
                        if (entry.Field!.Order < 0)
                        {
                            throw new InvalidOperationException(
                                $"{type.Name}.{entry.Property.Name} needs a field order: [DeviceField(0)], [DeviceField(1)], and so on.");
                        }

                        return new MessageField(entry.Property, entry.Field);
                    })
                    .OrderBy(entry => entry.Attribute.Order)
                    .ToArray();
                if (fields.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"{type.Name} has no message fields. Use a positional record, or mark settable properties with [DeviceField(order)].");
                }

                if (type.GetConstructor(Type.EmptyTypes) is null)
                {
                    throw new InvalidOperationException($"{type.Name} needs a public parameterless constructor to be parsed.");
                }
            }

            var unsupported = fields.FirstOrDefault(field => !MessageField.Supports(field.Property.PropertyType));
            if (unsupported is not null)
            {
                throw new InvalidOperationException(
                    $"{type.Name}.{unsupported.Name} is a {unsupported.Property.PropertyType.Name}, which a message field does not support; " +
                    "use a string, number, boolean, enum, date or time, and convert it in the device class.");
            }

            var layout = new MessageLayout(type, attribute, fields, constructor);
            if (layout._separator.Length == 0)
            {
                var unsized = fields.FirstOrDefault(field => field.Attribute.Width <= 0);
                if (unsized is not null)
                {
                    throw new InvalidOperationException(
                        $"{type.Name} has no separator, so every field needs a width; {unsized.Name} has none.");
                }
            }

            return layout;
        }

        public string Format(object message)
        {
            var values = _fields.Select(field => field.Write(field.Property.GetValue(message), _separator, _type));
            var parts = _prefix.Length > 0 ? values.Prepend(_prefix) : values;
            var text = string.Join(_separator, parts);
            return _checksum is null ? text : text + _checksum.Separator + _checksum.Compute(text);
        }

        public object Parse(string text)
        {
            var body = text;
            if (_checksum is not null)
            {
                var at = text.LastIndexOf(_checksum.Separator, StringComparison.Ordinal);
                if (at < 0)
                {
                    throw Mismatch(text, $"it has no '{_checksum.Separator}' before a checksum");
                }

                body = text[..at];
                var given = text[(at + _checksum.Separator.Length)..];
                var expected = _checksum.Compute(body);
                if (!string.Equals(given, expected, StringComparison.OrdinalIgnoreCase))
                {
                    throw Mismatch(text, $"its checksum is {given}, but the body computes to {expected}");
                }
            }

            if (!body.StartsWith(_prefix, StringComparison.Ordinal))
            {
                throw Mismatch(text, $"it does not start with '{_prefix}'");
            }

            var rest = body[_prefix.Length..];
            string[] raw;
            if (_separator.Length > 0)
            {
                if (_prefix.Length > 0)
                {
                    if (!rest.StartsWith(_separator, StringComparison.Ordinal))
                    {
                        throw Mismatch(text, $"'{_prefix}' is not followed by '{_separator}'");
                    }

                    rest = rest[_separator.Length..];
                }

                raw = rest.Split(_separator);
                if (raw.Length != _fields.Length)
                {
                    throw Mismatch(text, $"it has {raw.Length} fields, and {_type.Name} has {_fields.Length}");
                }
            }
            else
            {
                var total = _fields.Sum(field => field.Attribute.Width);
                if (rest.Length != total)
                {
                    throw Mismatch(text, $"it is {rest.Length} characters after the prefix, and {_type.Name}'s fields are {total}");
                }

                raw = new string[_fields.Length];
                var offset = 0;
                for (var index = 0; index < _fields.Length; index++)
                {
                    raw[index] = rest.Substring(offset, _fields[index].Attribute.Width);
                    offset += _fields[index].Attribute.Width;
                }
            }

            var values = new object?[_fields.Length];
            for (var index = 0; index < _fields.Length; index++)
            {
                values[index] = _fields[index].Read(raw[index], index, text, _type);
            }

            if (_constructor is not null)
            {
                return _constructor.Invoke(values);
            }

            var message = Activator.CreateInstance(_type)!;
            for (var index = 0; index < _fields.Length; index++)
            {
                _fields[index].Property.SetValue(message, values[index]);
            }

            return message;
        }

        private DeviceMessageFormatException Mismatch(string text, string reason)
            => new($"'{text}' is not a {_type.Name}: {reason}.");

        private static PropertyInfo? Find(PropertyInfo[] properties, string? name)
            => properties.FirstOrDefault(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class MessageField(PropertyInfo property, DeviceFieldAttribute attribute)
    {
        public PropertyInfo Property { get; } = property;

        public DeviceFieldAttribute Attribute { get; } = attribute;

        public string Name => Property.Name;

        private Type ValueType => Nullable.GetUnderlyingType(Property.PropertyType) ?? Property.PropertyType;

        public static bool Supports(Type propertyType)
        {
            var type = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
            return type == typeof(string) || type == typeof(bool) || type == typeof(char) || type == typeof(Guid) || type.IsEnum
                || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan)
                || type == typeof(DateOnly) || type == typeof(TimeOnly)
                || type.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, [typeof(string), typeof(NumberStyles), typeof(IFormatProvider)]) is not null;
        }

        public string Write(object? value, string separator, Type messageType)
        {
            var text = value is null ? string.Empty : ToText(value);
            if (separator.Length > 0 && text.Contains(separator, StringComparison.Ordinal))
            {
                throw new DeviceMessageFormatException(
                    $"{messageType.Name}.{Name} is '{text}', which contains the separator '{separator}', so the message could not be read back.");
            }

            if (Attribute.Width > 0)
            {
                if (text.Length > Attribute.Width)
                {
                    throw new DeviceMessageFormatException(
                        $"{messageType.Name}.{Name} is '{text}', longer than its width of {Attribute.Width}.");
                }

                text = Attribute.PadLeft ? text.PadLeft(Attribute.Width, Attribute.Pad) : text.PadRight(Attribute.Width, Attribute.Pad);
            }

            return text;
        }

        public object? Read(string raw, int index, string text, Type messageType)
        {
            var value = Attribute.Width > 0
                ? (Attribute.PadLeft ? raw.TrimStart(Attribute.Pad) : raw.TrimEnd(Attribute.Pad))
                : raw;
            if (value.Length == 0)
            {
                if (ValueType == typeof(string))
                {
                    return Property.PropertyType == typeof(string) ? string.Empty : null;
                }

                if (!Property.PropertyType.IsValueType || Nullable.GetUnderlyingType(Property.PropertyType) is not null)
                {
                    return null;
                }

                if (Attribute.Width > 0 && Attribute.PadLeft && Attribute.Pad == '0')
                {
                    value = "0";
                }
            }

            try
            {
                return FromText(value);
            }
            catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
            {
                throw new DeviceMessageFormatException(
                    $"'{text}' is not a {messageType.Name}: field {index} ({Name}) is '{raw}', which is not a {ValueType.Name}" +
                    (Attribute.Format is null ? "." : $" in the format '{Attribute.Format}'."),
                    exception);
            }
        }

        private string ToText(object value)
        {
            var culture = CultureInfo.InvariantCulture;
            var format = Attribute.Format;
            return value switch
            {
                string text => text,
                bool flag => Booleans(format).Split('|')[flag ? 0 : 1],
                Enum enumValue => format is null ? enumValue.ToString() : enumValue.ToString(format),
                IFormattable formattable => formattable.ToString(format, culture),
                _ => value.ToString() ?? string.Empty
            };
        }

        private object FromText(string value)
        {
            var type = ValueType;
            var culture = CultureInfo.InvariantCulture;
            var format = Attribute.Format;
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
                return format is null
                    ? DateTime.Parse(value, culture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal)
                    : DateTime.ParseExact(value, format, culture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
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

            // Every number type parses through the same invariant number styles.
            var parse = type.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, [typeof(string), typeof(NumberStyles), typeof(IFormatProvider)]);
            if (parse is not null)
            {
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

            throw new FormatException($"A {type.Name} field cannot be read.");
        }

        private static string Booleans(string? format)
            => format is not null && format.Count(character => character == '|') == 1 ? format : "1|0";
    }
}
