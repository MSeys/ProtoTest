namespace ProtoTest.Core;

using System.Globalization;
using System.Security.Cryptography;

internal sealed class NumericProtoTestIdGenerator : IProtoTestIdGenerator
{
    private readonly long _prefix;
    private readonly long _multiplier;
    private readonly long _maximumSequence;
    private readonly int _width;
    private long _sequence;

    public NumericProtoTestIdGenerator(ProtoTestIdOptions? options = null)
    {
        options ??= new ProtoTestIdOptions();
        if (options.SequenceDigits is < 1 or > 9)
            throw new ArgumentOutOfRangeException(nameof(options.SequenceDigits), "SequenceDigits must be between 1 and 9.");

        _prefix = options.RunPrefix ?? RandomNumberGenerator.GetInt32(100_000, 1_000_000);
        if (_prefix < 0) throw new ArgumentOutOfRangeException(nameof(options.RunPrefix));

        _multiplier = Pow10(options.SequenceDigits);
        _maximumSequence = _multiplier - 1;
        _width = _prefix.ToString(CultureInfo.InvariantCulture).Length + options.SequenceDigits;
        if (_width > 18)
            throw new ArgumentOutOfRangeException(nameof(options.RunPrefix), "The run prefix and sequence may contain at most 18 digits.");
    }

    public ProtoTestId Next(System.Reflection.MethodInfo testMethod)
    {
        ArgumentNullException.ThrowIfNull(testMethod);
        var sequence = Interlocked.Increment(ref _sequence);
        if (sequence > _maximumSequence)
            throw new InvalidOperationException("The ProtoTest ID sequence for this host is exhausted.");

        return new ProtoTestId(checked((_prefix * _multiplier) + sequence), _width);
    }

    private static long Pow10(int exponent)
    {
        var result = 1L;
        for (var i = 0; i < exponent; i++) result *= 10;
        return result;
    }
}
