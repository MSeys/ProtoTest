namespace ProtoTest.Rest.Matching;

using System.Text.RegularExpressions;

public static class IsRest
{
    public static IValueMatcher NotNull() => new NotNullMatcher();
    public static IValueMatcher Null() => new NullMatcher();
    public static IValueMatcher Any() => new AnyMatcher();

    public static IValueMatcher GreaterThan<T>(T threshold) where T : IComparable
        => new ComparableMatcher<T>(threshold, (cmp) => cmp > 0, "greater than");

    public static IValueMatcher LessThan<T>(T threshold) where T : IComparable
        => new ComparableMatcher<T>(threshold, (cmp) => cmp < 0, "less than");

    public static IValueMatcher GreaterThanOrEqualTo<T>(T threshold) where T : IComparable
        => new ComparableMatcher<T>(threshold, (cmp) => cmp >= 0, "greater than or equal to");

    public static IValueMatcher Regex(string pattern, RegexOptions options = RegexOptions.None)
        => new RegexMatcher(pattern, options);

    public static IValueMatcher StringMatching(Predicate<string?> predicate, string description = "custom string condition")
        => new CustomPredicateMatcher<string>(predicate, description);

    // --- Private Matcher Implementations ---

    private sealed class NotNullMatcher : IValueMatcher
    {
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

    private sealed class NullMatcher : IValueMatcher
    {
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

    private sealed class AnyMatcher : IValueMatcher
    {
        public bool Matches(object? actual, out string? errorMessage)
        {
            errorMessage = null;
            return true;
        }
    }

    private sealed class ComparableMatcher<T>(T threshold, Func<int, bool> condition, string operatorName) : IValueMatcher
        where T : IComparable
    {
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

    private sealed class RegexMatcher(string pattern, RegexOptions options) : IValueMatcher
    {
        private readonly Regex _regex = new(pattern, options);

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

    private sealed class CustomPredicateMatcher<T>(Predicate<T?> predicate, string description) : IValueMatcher
    {
        public bool Matches(object? actual, out string? errorMessage)
        {
            T? typedVal = actual is T val ? val : default;
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