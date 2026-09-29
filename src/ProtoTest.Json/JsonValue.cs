namespace ProtoTest.Json;

using System.Globalization;
using System.Text.RegularExpressions;

/// <summary>
/// Matches one JSON scalar value and describes the expectation on mismatch, so a failed assertion says
/// what it wanted.
/// </summary>
public interface IJsonValueMatcher
{
    /// <summary>The expectation phrased for a failure message.</summary>
    string Description { get; }

    /// <summary>
    /// Tests <paramref name="actual"/> and, on mismatch, sets <paramref name="errorMessage"/> to what
    /// was expected against what was found.
    /// </summary>
    bool Matches(object? actual, out string? errorMessage);
}

/// <summary>The matchers an assertion can hold a JSON scalar to.</summary>
public static class JsonValue
{
    /// <summary>Matches any value, null included.</summary>
    public static IJsonValueMatcher Any() => Match("any value", _ => true);

    /// <summary>Matches any value except null.</summary>
    public static IJsonValueMatcher NotNull() => Match("not null", value => value is not null);

    /// <summary>Matches a missing or explicit null.</summary>
    public static IJsonValueMatcher Null() => Match("null", value => value is null);

    /// <summary>Matches a string against a regular expression.</summary>
    public static IJsonValueMatcher Regex(string pattern, RegexOptions options = RegexOptions.None)
        => Match($"matches /{pattern}/", value => value is string text && System.Text.RegularExpressions.Regex.IsMatch(text, pattern, options));

    /// <summary>Matches a string containing <paramref name="expected"/>.</summary>
    public static IJsonValueMatcher StringContaining(string expected, StringComparison comparison = StringComparison.Ordinal)
        => Match($"contains \"{expected}\"", value => value is string text && text.Contains(expected, comparison));

    /// <summary>Matches a string starting with <paramref name="expected"/>.</summary>
    public static IJsonValueMatcher StringStartingWith(string expected, StringComparison comparison = StringComparison.Ordinal)
        => Match($"starts with \"{expected}\"", value => value is string text && text.StartsWith(expected, comparison));

    /// <summary>Matches a string ending with <paramref name="expected"/>.</summary>
    public static IJsonValueMatcher StringEndingWith(string expected, StringComparison comparison = StringComparison.Ordinal)
        => Match($"ends with \"{expected}\"", value => value is string text && text.EndsWith(expected, comparison));

    /// <summary>Matches a string the <paramref name="predicate"/> accepts, phrased as
    /// <paramref name="description"/> on mismatch.</summary>
    public static IJsonValueMatcher StringMatching(Predicate<string?> predicate, string description = "custom string condition")
        => Match(description, value => predicate(value as string));

    /// <summary>Matches a value above <paramref name="threshold"/>; a null or incomparable value does not match.</summary>
    public static IJsonValueMatcher GreaterThan<T>(T threshold) where T : IComparable
        => Compare(threshold, comparison => comparison > 0, "greater than");

    /// <summary>Matches a value below <paramref name="threshold"/>; a null or incomparable value does not match.</summary>
    public static IJsonValueMatcher LessThan<T>(T threshold) where T : IComparable
        => Compare(threshold, comparison => comparison < 0, "less than");

    /// <summary>Matches a value at or above <paramref name="threshold"/>; a null or incomparable value does not match.</summary>
    public static IJsonValueMatcher GreaterThanOrEqualTo<T>(T threshold) where T : IComparable
        => Compare(threshold, comparison => comparison >= 0, "greater than or equal to");

    /// <summary>Matches a value at or below <paramref name="threshold"/>; a null or incomparable value does not match.</summary>
    public static IJsonValueMatcher LessThanOrEqualTo<T>(T threshold) where T : IComparable
        => Compare(threshold, comparison => comparison <= 0, "less than or equal to");

    /// <summary>Matches a value within an inclusive range.</summary>
    public static IJsonValueMatcher Between<T>(T minimum, T maximum) where T : IComparable
        => Match($"between {minimum} and {maximum} (inclusive)", value =>
            TryCompare(value, minimum, out var lower) && lower >= 0 &&
            TryCompare(value, maximum, out var upper) && upper <= 0);

    /// <summary>Matches a value equal to one of <paramref name="expected"/>; numeric types may differ,
    /// every other type must be compatible.</summary>
    public static IJsonValueMatcher OneOf<T>(params T[] expected)
        => Match($"one of [{string.Join(", ", expected)}]", value => expected.Any(item => ScalarEquals(value, item)));

    /// <summary>Matches a value the <paramref name="predicate"/> accepts, phrased as
    /// <paramref name="description"/> on mismatch.</summary>
    public static IJsonValueMatcher Matching(Predicate<object?> predicate, string description)
        => Match(description, predicate);

    private static IJsonValueMatcher Compare<T>(T threshold, Func<int, bool> predicate, string operation) where T : IComparable
        => Match($"{operation} {threshold}", value => TryCompare(value, threshold, out var comparison) && predicate(comparison));

    private static bool TryCompare<T>(object? actual, T expected, out int comparison) where T : IComparable
    {
        comparison = 0;
        if (actual is null) return false;
        try
        {
            comparison = ((T)Convert.ChangeType(actual, typeof(T), CultureInfo.InvariantCulture)).CompareTo(expected);
            return true;
        }
        catch { return false; }
    }

    private static bool ScalarEquals<T>(object? actual, T expected)
    {
        if (Equals(actual, expected)) return true;
        if (actual is null || expected is null) return false;
        var expectedType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        var actualType = Nullable.GetUnderlyingType(actual.GetType()) ?? actual.GetType();
        // OneOf is a scalar constraint: numeric types may differ because JSON numbers surface as
        // decimal, but everything else must be type-compatible, so the string "2" never matches 2.
        if (!JsonScalarTypes.IsNumeric(actualType) || !JsonScalarTypes.IsNumeric(expectedType)) return false;
        try
        {
            // decimal is exact for both integral and decimal values, so 2.0m and 2 compare equal while
            // no precision is lost to a double round-trip.
            return Convert.ToDecimal(actual, CultureInfo.InvariantCulture)
                == Convert.ToDecimal(expected, CultureInfo.InvariantCulture);
        }
        catch (OverflowException) { return false; }
        catch (InvalidCastException) { return false; }
    }

    private static IJsonValueMatcher Match(string description, Predicate<object?> predicate)
        => new PredicateMatcher(description, predicate);

    private sealed record PredicateMatcher(string Description, Predicate<object?> Predicate) : IJsonValueMatcher
    {
        public bool Matches(object? actual, out string? errorMessage)
        {
            if (Predicate(actual)) { errorMessage = null; return true; }
            errorMessage = $"Expected {Description}, but found {Format(actual)}.";
            return false;
        }

        private static string Format(object? value) => value is null ? "NULL" : $"'{value}'";
    }
}
