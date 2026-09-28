namespace ProtoTest.Sheets.Internal;

/// <summary>
/// The one place a cell's typed value becomes a .NET value. A value that does not fit the target type
/// is a value problem rather than an exception to swallow: <see cref="TryConvert"/> answers false and
/// <see cref="Convert"/> names the cell, its value and the target type.
/// </summary>
internal static class SheetCellValue
{
    /// <summary>Whether a type can hold an empty cell: a reference type or a nullable value type.</summary>
    public static bool IsNullable(Type type)
        => !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;

    /// <summary>Converts a cell to <paramref name="type"/>; a value that cannot convert fails the read.</summary>
    public static object? Convert(Type type, ProtoCell cell)
    {
        if (TryConvert(type, cell, out var value))
        {
            return value;
        }

        throw new FormatException(
            $"'{cell.Display()}' at {cell.Reference} cannot be converted to " +
            $"{(Nullable.GetUnderlyingType(type) ?? type).Name}.");
    }

    /// <summary>
    /// Converts a cell to <paramref name="type"/>. An empty cell converts to <see langword="null"/>; a
    /// non-empty cell whose typed value does not fit answers false.
    /// </summary>
    public static bool TryConvert(Type type, ProtoCell cell, out object? value)
    {
        value = null;
        if (cell.IsEmpty)
        {
            return true;
        }

        var target = Nullable.GetUnderlyingType(type) ?? type;
        if (target == typeof(string))
        {
            value = cell.Text ?? cell.Display();
            return true;
        }

        if (target == typeof(decimal) && cell.Number is { } decimalNumber)
        {
            value = (decimal)decimalNumber;
            return true;
        }

        if (target == typeof(double) && cell.Number is { } doubleNumber)
        {
            value = doubleNumber;
            return true;
        }

        if (target == typeof(int) && cell.Number is { } intNumber)
        {
            if (!TryIntegral(intNumber, out var integral) || integral is < int.MinValue or > int.MaxValue)
            {
                return false;
            }

            value = (int)integral;
            return true;
        }

        if (target == typeof(long) && cell.Number is { } longNumber)
        {
            if (!TryIntegral(longNumber, out var integral))
            {
                return false;
            }

            value = integral;
            return true;
        }

        if (target == typeof(bool) && cell.Boolean is { } boolean)
        {
            value = boolean;
            return true;
        }

        if (target == typeof(DateTime) && cell.Date is { } date)
        {
            value = date;
            return true;
        }

        return false;
    }

    /// <summary>
    /// An integer target accepts only a finite, integral value that fits: <c>1200.75</c> is not
    /// <c>1200</c>, <c>1e20</c> does not become <c>long.MinValue</c>, and NaN never becomes an integer.
    /// A float that is integral up to a rounding tolerance (a spreadsheet render of a binary number)
    /// still converts.
    /// </summary>
    private static bool TryIntegral(double number, out long value)
    {
        value = 0;
        if (!double.IsFinite(number))
        {
            return false;
        }

        var rounded = Math.Round(number);
        if (Math.Abs(number - rounded) > IntegralTolerance)
        {
            return false;
        }

        // 2^63 is representable as a double but outside long; the bound keeps the cast defined.
        if (rounded < -9223372036854775808.0 || rounded >= 9223372036854775808.0)
        {
            return false;
        }

        value = (long)rounded;
        return true;
    }

    /// <summary>The tolerance between a cell's double and an exact integer, in absolute value.</summary>
    private const double IntegralTolerance = 1e-9;
}
