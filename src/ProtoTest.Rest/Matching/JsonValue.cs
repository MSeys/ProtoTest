namespace ProtoTest.Rest.Matching;

using System.Text.RegularExpressions;

public static class JsonValue
{
    public static IJsonValueMatcher NotNull() => new NotNullMatcher();
    public static IJsonValueMatcher Null() => new NullMatcher();
    public static IJsonValueMatcher Any() => new AnyMatcher();

    public static IJsonValueMatcher GreaterThan<T>(T threshold) where T : IComparable
        => new ComparableMatcher<T>(threshold, (cmp) => cmp > 0, "greater than");

    public static IJsonValueMatcher LessThan<T>(T threshold) where T : IComparable
        => new ComparableMatcher<T>(threshold, (cmp) => cmp < 0, "less than");

    public static IJsonValueMatcher GreaterThanOrEqualTo<T>(T threshold) where T : IComparable
        => new ComparableMatcher<T>(threshold, (cmp) => cmp >= 0, "greater than or equal to");

    public static IJsonValueMatcher LessThanOrEqualTo<T>(T threshold) where T : IComparable
        => new ComparableMatcher<T>(threshold, comparison => comparison <= 0, "less than or equal to");

    public static IJsonValueMatcher Between<T>(T minimum, T maximum) where T : IComparable
        => new CustomPredicateMatcher<T>(
            value => value is not null && value.CompareTo(minimum) >= 0 && value.CompareTo(maximum) <= 0,
            $"between {minimum} and {maximum} (inclusive)");

    public static IJsonValueMatcher Regex(string pattern, RegexOptions options = RegexOptions.None)
        => new RegexMatcher(pattern, options);

    public static IJsonValueMatcher StringMatching(Predicate<string?> predicate, string description = "custom string condition")
        => new CustomPredicateMatcher<string>(predicate, description);

    public static IJsonValueMatcher StringContaining(string expected, StringComparison comparison = StringComparison.Ordinal)
        => new CustomPredicateMatcher<string>(
            value => value?.Contains(expected, comparison) == true,
            $"contains \"{expected}\"");

    public static IJsonValueMatcher StringStartingWith(string expected, StringComparison comparison = StringComparison.Ordinal)
        => new CustomPredicateMatcher<string>(
            value => value?.StartsWith(expected, comparison) == true,
            $"starts with \"{expected}\"");

    public static IJsonValueMatcher StringEndingWith(string expected, StringComparison comparison = StringComparison.Ordinal)
        => new CustomPredicateMatcher<string>(
            value => value?.EndsWith(expected, comparison) == true,
            $"ends with \"{expected}\"");

    public static IJsonValueMatcher OneOf<T>(params T[] expected)
        => new CustomPredicateMatcher<T>(
            value => expected.Contains(value),
            $"one of [{string.Join(", ", expected)}]");

    // --- Private Matcher Implementations ---

    private sealed class NotNullMatcher : IJsonValueMatcher
    {
        public string Description => "not null";

        public bool Matches(object? actual, out string? errorMessage)
        {
            if (actual is null)
            {
                errorMessage = "Expected value to be NOT NULL, but found NULL.";
                return false;
            }
            errorMessage = null;
            return true;
        }
    }

    private sealed class NullMatcher : IJsonValueMatcher
    {
        public string Description => "null";

        public bool Matches(object? actual, out string? errorMessage)
        {
            if (actual is not null)
            {
                errorMessage = $"Expected NULL, but found '{actual}'.";
                return false;
            }
            errorMessage = null;
            return true;
        }
    }

    private sealed class AnyMatcher : IJsonValueMatcher
    {
        public string Description => "any value";

        public bool Matches(object? actual, out string? errorMessage)
        {
            errorMessage = null;
            return true;
        }
    }

    private sealed class ComparableMatcher<T>(T threshold, Func<int, bool> condition, string operatorName) : IJsonValueMatcher
        where T : IComparable
    {
        public string Description => $"{operatorName} {threshold}";

        public bool Matches(object? actual, out string? errorMessage)
        {
            if (actual is null)
            {
                errorMessage = $"Expected value {operatorName} '{threshold}', but found NULL.";
                return false;
            }

            try
            {
                var converted = (T)Convert.ChangeType(actual, typeof(T));
                var comparison = converted.CompareTo(threshold);

                if (!condition(comparison))
                {
                    errorMessage = $"Expected value {operatorName} '{threshold}', but found '{actual}'.";
                    return false;
                }
            }
            catch (Exception)
            {
                errorMessage = $"Could not convert actual value '{actual}' (type {actual.GetType().Name}) to {typeof(T).Name} for comparison.";
                return false;
            }

            errorMessage = null;
            return true;
        }
    }

    private sealed class RegexMatcher(string pattern, RegexOptions options) : IJsonValueMatcher
    {
        private readonly Regex _regex = new(pattern, options);
        public string Description => $"matches /{pattern}/ ({options})";

        public bool Matches(object? actual, out string? errorMessage)
        {
            var str = actual?.ToString();
            if (str is null || !_regex.IsMatch(str))
            {
                errorMessage = $"Expected string matching regex '{pattern}', but found '{actual ?? "NULL"}'.";
                return false;
            }
            errorMessage = null;
            return true;
        }
    }

    private sealed class CustomPredicateMatcher<T>(Predicate<T?> predicate, string description) : IJsonValueMatcher
    {
        public string Description => description;

        public bool Matches(object? actual, out string? errorMessage)
        {
            T? typedVal;
            try
            {
                typedVal = actual is T value
                    ? value
                    : actual is null
                        ? default
                        : (T?)Convert.ChangeType(actual, typeof(T));
            }
            catch (Exception)
            {
                errorMessage = $"Could not convert actual value '{actual}' to {typeof(T).Name} for condition '{description}'.";
                return false;
            }

            if (!predicate(typedVal))
            {
                errorMessage = $"Value '{actual ?? "NULL"}' failed condition: '{description}'.";
                return false;
            }
            errorMessage = null;
            return true;
        }
    }
}
