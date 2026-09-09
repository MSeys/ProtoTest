namespace ProtoTest.Core;

using System.Globalization;
using System.Reflection;

/// <summary>
/// Numeric identifier for one test execution. Its string representation preserves the configured width.
/// </summary>
public readonly record struct ProtoTestId
{
    public ProtoTestId(long number, int width = 1)
    {
        if (number < 0) throw new ArgumentOutOfRangeException(nameof(number));
        if (width is < 1 or > 18) throw new ArgumentOutOfRangeException(nameof(width));
        if (number.ToString(CultureInfo.InvariantCulture).Length > width)
            throw new ArgumentOutOfRangeException(nameof(width), "Width is too small for the numeric identifier.");

        Number = number;
        Width = width;
    }

    public long Number { get; }
    public int Width { get; }
    public string Value => Number.ToString($"D{Width}", CultureInfo.InvariantCulture);
    public override string ToString() => Value;

    public static ProtoTestId Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > 18 || !value.All(char.IsAsciiDigit)
            || !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            throw new FormatException("A ProtoTest ID must contain between 1 and 18 decimal digits.");
        }

        return new ProtoTestId(number, value.Length);
    }
}

public sealed class ProtoTestIdOptions
{
    /// <summary>A numeric prefix unique to a run, build, or worker. A random six-digit prefix is used by default.</summary>
    public long? RunPrefix { get; set; }

    /// <summary>Number of digits reserved for the sequence within a host.</summary>
    public int SequenceDigits { get; set; } = 6;
}

public interface IProtoTestIdGenerator
{
    ProtoTestId Next(MethodInfo testMethod);
}
